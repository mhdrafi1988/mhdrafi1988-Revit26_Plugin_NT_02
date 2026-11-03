namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>What a board piece is, in the contour-row layout.</summary>
    public enum PieceKind
    {
        /// <summary>Uncut board (length × width).</summary>
        Full,

        /// <summary>Cut along the hip at the start of a row; mirror image of the neighbouring face's <see cref="HipRight"/>.</summary>
        HipLeft,

        /// <summary>Cut along the hip at the end of a row; mirror image of the neighbouring face's <see cref="HipLeft"/>.</summary>
        HipRight,

        /// <summary>Make-up piece in the middle of a row.</summary>
        Filler,

        /// <summary>Piece in the last, narrower row at the ridge / apex.</summary>
        RidgeRow,

        /// <summary>Any other cut piece (single grid modes, or a row with only one piece).</summary>
        Cut
    }
}
