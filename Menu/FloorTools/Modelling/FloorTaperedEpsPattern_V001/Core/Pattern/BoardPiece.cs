namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>One board (full or cut) of the grid, with its thickness range.</summary>
    public sealed record BoardPiece
    {
        public string Id { get; init; } = string.Empty;
        public int Row { get; init; }
        public int Column { get; init; }
        public Polygon2D Polygon { get; init; } = Polygon2D.Empty;
        public bool IsFull { get; init; }
        public double MinThicknessMm { get; init; }
        public double MaxThicknessMm { get; init; }
        public string Code { get; init; } = string.Empty;

        /// <summary>Floor edge whose grid this board belongs to ("each edge" mode), otherwise null.</summary>
        public int? EdgeIndex { get; init; }

        /// <summary>Full / hip / filler / ridge-row / cut. Single grid modes use Full or Cut.</summary>
        public PieceKind Kind { get; init; } = PieceKind.Cut;

        /// <summary>
        /// Shape key in the piece's own row frame (mm, hip pieces mirrored to one hand), used to give
        /// identical pieces the same code. Empty in single grid modes.
        /// </summary>
        public string ShapeKey { get; init; } = string.Empty;

        /// <summary>Contour-rows mode: extent of the piece along its row (m). Never more than the board length.</summary>
        public double SpanAlongRowM { get; init; }
    }
}
