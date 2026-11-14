using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models;
using System;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers
{
    /// <summary>
    /// Pure geometry helpers. No Revit transactions, no UI references.
    /// All public methods work in mm; Revit internal units are feet, so
    /// conversion happens once, right when a value is read off the element.
    /// </summary>
    internal static class BoundingBoxCalculator
    {
        private const double FeetToMm = 304.8;

        /// <summary>
        /// Reads an element's bounding box (as seen in the given view) and
        /// converts it to (x, y, width, height) in mm, in view coordinates:
        /// x along the view's right direction, y along its up direction.
        /// All eight corners are projected, so this works in plans, sections, elevations,
        /// drafting views, legends and sheets. In an unrotated plan it equals model X / Y (same as V004).
        /// Returns null if the element has no bounding box in this view (e.g. hidden).
        /// </summary>
        public static (double x, double y, double width, double height)? GetBoundingBox(Element elem, View view)
        {
            BoundingBoxXYZ bbox = elem.get_BoundingBox(view);
            if (bbox == null)
                return null;

            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            double minR = double.MaxValue, maxR = double.MinValue;
            double minU = double.MaxValue, maxU = double.MinValue;
            foreach (double px in new[] { bbox.Min.X, bbox.Max.X })
            {
                foreach (double py in new[] { bbox.Min.Y, bbox.Max.Y })
                {
                    foreach (double pz in new[] { bbox.Min.Z, bbox.Max.Z })
                    {
                        var p = new XYZ(px, py, pz);
                        double r = p.DotProduct(right);
                        double u = p.DotProduct(up);
                        minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                        minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                    }
                }
            }

            return (minR * FeetToMm, minU * FeetToMm, (maxR - minR) * FeetToMm, (maxU - minU) * FeetToMm);
        }

        /// <summary>Converts an <see cref="AnnotationData"/> to the Revit-free box the resolver works on.</summary>
        public static ResolveBox ToResolveBox(AnnotationData data) => new ResolveBox
        {
            Id = data.ElementId.Value,
            X = data.X,
            Y = data.Y,
            Width = data.Width,
            Height = data.Height
        };

        /// <summary>Converts mm to Revit internal feet.</summary>
        public static double MmToFeet(double mm) => mm / FeetToMm;

        /// <summary>
        /// Insertion point in mm - defaults to the bounding box min corner.
        /// (Elements with a LocationPoint could be handled more precisely by
        /// the caller before falling back to this.)
        /// </summary>
        public static (double x, double y) GetInsertionPoint(AnnotationData data)
        {
            return (data.X, data.Y);
        }

        /// <summary>
        /// Axis-aligned bounding box intersection test. Overlap area must be > 0,
        /// so boxes that merely touch at an edge do not count as overlapping.
        /// </summary>
        public static bool DoBoxesIntersect(AnnotationData a, AnnotationData b) => DoBoxesIntersect(a, b, 0);

        /// <summary>
        /// Overlap test with a threshold (6.1.0): the boxes must overlap by more than
        /// <paramref name="minOverlapMm"/> on both axes. 0 = any overlap with area &gt; 0 (as before).
        /// </summary>
        public static bool DoBoxesIntersect(AnnotationData a, AnnotationData b, double minOverlapMm)
        {
            double overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            double overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            double min = Math.Max(0, minOverlapMm);
            return overlapX > min && overlapY > min;
        }

        /// <summary>
        /// Vertical and horizontal gap between two boxes, in mm.
        /// 0 means the boxes touch/overlap on that axis; positive means separated.
        /// </summary>
        public static (double vGap, double hGap) CalculateGap(AnnotationData a, AnnotationData b)
        {
            double vGap = 0;
            if (a.Bottom <= b.Y)
                vGap = b.Y - a.Bottom;
            else if (b.Bottom <= a.Y)
                vGap = a.Y - b.Bottom;

            double hGap = 0;
            if (a.Right <= b.X)
                hGap = b.X - a.Right;
            else if (b.Right <= a.X)
                hGap = a.X - b.Right;

            return (Math.Round(vGap, 2), Math.Round(hGap, 2));
        }
    }
}
