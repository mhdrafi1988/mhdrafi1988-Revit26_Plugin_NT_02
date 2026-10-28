using System;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout
{
    /// <summary>Axis-aligned rectangle in sheet space (feet, Y pointing up).</summary>
    public readonly struct LayoutRect
    {
        /// <summary>Left edge.</summary>
        public double MinX { get; }

        /// <summary>Bottom edge.</summary>
        public double MinY { get; }

        /// <summary>Right edge.</summary>
        public double MaxX { get; }

        /// <summary>Top edge.</summary>
        public double MaxY { get; }

        /// <summary>Creates a rectangle from its four edges.</summary>
        public LayoutRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>MaxX − MinX.</summary>
        public double Width => MaxX - MinX;

        /// <summary>MaxY − MinY.</summary>
        public double Height => MaxY - MinY;

        /// <summary>True when the rectangle has a positive, finite width and height.</summary>
        public bool IsValid => double.IsFinite(MinX) && double.IsFinite(MinY) && double.IsFinite(MaxX)
                               && double.IsFinite(MaxY) && Width > 0 && Height > 0;

        /// <summary>The rectangle moved inward by the given amounts (negative values move it outward).</summary>
        public LayoutRect Inset(double left, double bottom, double right, double top)
            => new(MinX + left, MinY + bottom, MaxX - right, MaxY - top);

        /// <summary>A rectangle of <paramref name="width"/> × <paramref name="height"/> with its bottom-left corner at (x, y).</summary>
        public static LayoutRect FromCorner(double x, double y, double width, double height)
            => new(x, y, x + width, y + height);

        /// <summary>The smallest rectangle holding both.</summary>
        public LayoutRect Union(LayoutRect other)
            => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
                   Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }
}
