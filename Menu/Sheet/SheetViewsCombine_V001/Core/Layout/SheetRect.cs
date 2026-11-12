using System;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout
{
    /// <summary>Axis-aligned rectangle in sheet space (feet, Y pointing up). Revit-free.</summary>
    public readonly struct SheetRect : IEquatable<SheetRect>
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
        public SheetRect(double minX, double minY, double maxX, double maxY)
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

        /// <summary>Width × height.</summary>
        public double Area => Width * Height;

        /// <summary>True when every edge is finite and the width and height are positive.</summary>
        public bool IsValid => double.IsFinite(MinX) && double.IsFinite(MinY) && double.IsFinite(MaxX)
                               && double.IsFinite(MaxY) && Width > 0 && Height > 0;

        /// <summary>A rectangle of <paramref name="width"/> × <paramref name="height"/> with its top-left corner at (x, y).</summary>
        public static SheetRect FromTopLeft(double x, double top, double width, double height)
            => new(x, top - height, x + width, top);

        /// <summary>The rectangle moved inward by the given amounts (negative values move it outward).</summary>
        public SheetRect Inset(double left, double bottom, double right, double top)
            => new(MinX + left, MinY + bottom, MaxX - right, MaxY - top);

        /// <summary>The rectangle grown by <paramref name="dx"/> left and right and <paramref name="dy"/> up and down.</summary>
        public SheetRect Grow(double dx, double dy) => new(MinX - dx, MinY - dy, MaxX + dx, MaxY + dy);

        /// <summary>The rectangle shifted by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
        public SheetRect Offset(double dx, double dy) => new(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);

        /// <summary>True when the two rectangles share interior area (touching edges do not count).</summary>
        public bool Overlaps(SheetRect other, double tolerance = 1e-9)
            => MinX < other.MaxX - tolerance && other.MinX < MaxX - tolerance
               && MinY < other.MaxY - tolerance && other.MinY < MaxY - tolerance;

        /// <summary>True when <paramref name="inner"/> lies inside this rectangle (within <paramref name="tolerance"/>).</summary>
        public bool Contains(SheetRect inner, double tolerance = 1e-9)
            => inner.MinX >= MinX - tolerance && inner.MaxX <= MaxX + tolerance
               && inner.MinY >= MinY - tolerance && inner.MaxY <= MaxY + tolerance;

        /// <summary>The smallest rectangle holding both.</summary>
        public SheetRect Union(SheetRect other)
            => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
                   Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

        /// <inheritdoc/>
        public bool Equals(SheetRect other)
            => MinX.Equals(other.MinX) && MinY.Equals(other.MinY) && MaxX.Equals(other.MaxX) && MaxY.Equals(other.MaxY);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SheetRect r && Equals(r);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(MinX, MinY, MaxX, MaxY);

        /// <inheritdoc/>
        public override string ToString() => $"[{MinX:0.####},{MinY:0.####} – {MaxX:0.####},{MaxY:0.####}]";
    }
}
