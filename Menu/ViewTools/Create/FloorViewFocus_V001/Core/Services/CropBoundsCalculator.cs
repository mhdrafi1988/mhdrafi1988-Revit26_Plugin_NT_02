using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Services
{
    /// <summary>2D rectangle in view-local coordinates. Units: feet (Revit internal).</summary>
    public readonly record struct Bounds2D(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    /// <summary>
    /// Pure geometry / unit maths for the crop box. Zero Revit API dependency.
    /// </summary>
    public static class CropBoundsCalculator
    {
        public const double MmPerFoot = 304.8;

        /// <summary>Revit API minimum annotation crop offset: 1/96 ft (1/8").</summary>
        public const double MinAnnotationOffsetFeet = 1.0 / 96.0;

        public static double MmToFeet(double mm) => mm / MmPerFoot;
        public static double FeetToMm(double feet) => feet * MmPerFoot;

        /// <summary>Smallest rectangle containing every input rectangle.</summary>
        public static Bounds2D Union(IEnumerable<Bounds2D> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            bool any = false;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (Bounds2D b in items)
            {
                any = true;
                if (b.MinX < minX) minX = b.MinX;
                if (b.MinY < minY) minY = b.MinY;
                if (b.MaxX > maxX) maxX = b.MaxX;
                if (b.MaxY > maxY) maxY = b.MaxY;
            }

            if (!any) throw new InvalidOperationException("No bounds to union.");
            return new Bounds2D(minX, minY, maxX, maxY);
        }

        /// <summary>Grows the rectangle outward by <paramref name="marginFeet"/> on every side.</summary>
        public static Bounds2D Expand(Bounds2D b, double marginFeet)
            => new(b.MinX - marginFeet, b.MinY - marginFeet, b.MaxX + marginFeet, b.MaxY + marginFeet);

        /// <summary>Clamps an annotation crop offset (feet) to the Revit API minimum.</summary>
        public static double ClampAnnotationOffset(double feet, out bool wasClamped)
        {
            wasClamped = feet < MinAnnotationOffsetFeet;
            return wasClamped ? MinAnnotationOffsetFeet : feet;
        }
    }
}
