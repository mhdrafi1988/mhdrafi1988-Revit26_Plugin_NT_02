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

        /// <summary>Width × height (0 when not valid).</summary>
        public double Area => IsValid ? Width * Height : 0;

        /// <summary>True when the two rectangles overlap with a positive area (touching edges don't count).</summary>
        public bool Intersects(LayoutRect other)
            => MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;

        /// <summary>The overlap of the two rectangles, or null when they don't overlap.</summary>
        public LayoutRect? Intersect(LayoutRect other)
        {
            var r = new LayoutRect(Math.Max(MinX, other.MinX), Math.Max(MinY, other.MinY),
                                   Math.Min(MaxX, other.MaxX), Math.Min(MaxY, other.MaxY));
            return r.IsValid ? r : null;
        }

        /// <summary>The rectangle grown by <paramref name="dx"/> left and right and <paramref name="dy"/> up and down.</summary>
        public LayoutRect Grow(double dx, double dy) => new(MinX - dx, MinY - dy, MaxX + dx, MaxY + dy);

        /// <summary>The smallest rectangle holding both.</summary>
        public LayoutRect Union(LayoutRect other)
            => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
                   Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }
}
