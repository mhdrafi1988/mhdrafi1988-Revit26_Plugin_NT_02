using System;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout
{
    /// <summary>
    /// Axis-aligned rectangle in sheet space, in Revit's internal units (feet), Y up.
    /// Revit-free on purpose so the layout maths can be unit tested
    /// (<c>Tests/SheetAlignDistribute.Tests</c>).
    /// </summary>
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

        /// <summary>Horizontal centre.</summary>
        public double CenterX => (MinX + MaxX) / 2.0;

        /// <summary>Vertical centre.</summary>
        public double CenterY => (MinY + MaxY) / 2.0;

        /// <summary>True when the rectangle has a positive width and height.</summary>
        public bool IsValid => Width > 0 && Height > 0;

        /// <summary>The same rectangle moved by <paramref name="dx"/> / <paramref name="dy"/>.</summary>
        public LayoutRect Offset(double dx, double dy) => new(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);

        /// <summary>The same size, with its bottom-left corner moved to <paramref name="minX"/> / <paramref name="minY"/>.</summary>
        public LayoutRect MoveTo(double minX, double minY) => new(minX, minY, minX + Width, minY + Height);

        /// <summary>Shrunk by the four margins. May become invalid when the margins are larger than the rectangle.</summary>
        public LayoutRect Inset(double left, double bottom, double right, double top)
            => new(MinX + left, MinY + bottom, MaxX - right, MaxY - top);

        /// <summary>
        /// True when this rectangle and <paramref name="other"/> share area. Rectangles that only
        /// touch do not overlap: <paramref name="tolerance"/> is how far they may bite into each
        /// other before it counts.
        /// </summary>
        public bool Overlaps(LayoutRect other, double tolerance = 0)
            => MinX < other.MaxX - tolerance && other.MinX < MaxX - tolerance
            && MinY < other.MaxY - tolerance && other.MinY < MaxY - tolerance;

        /// <summary>Length the two rectangles share on the Y axis; 0 or less when they do not.</summary>
        public double VerticalOverlap(LayoutRect other) => Math.Min(MaxY, other.MaxY) - Math.Max(MinY, other.MinY);

        /// <summary>Length the two rectangles share on the X axis; 0 or less when they do not.</summary>
        public double HorizontalOverlap(LayoutRect other) => Math.Min(MaxX, other.MaxX) - Math.Max(MinX, other.MinX);

        /// <summary>True when this rectangle lies inside <paramref name="outer"/>, allowing <paramref name="tolerance"/> of slack.</summary>
        public bool IsInside(LayoutRect outer, double tolerance = 0)
            => MinX >= outer.MinX - tolerance && MaxX <= outer.MaxX + tolerance
            && MinY >= outer.MinY - tolerance && MaxY <= outer.MaxY + tolerance;

        /// <summary>The smallest rectangle holding both.</summary>
        public LayoutRect Union(LayoutRect other) => new(
            Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
    }
}
