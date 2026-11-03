namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>How pieces are colour-coded with filled regions.</summary>
    public enum HatchMode
    {
        None,

        /// <summary>Two slots: odd rows / even rows (each row is one thickness band).</summary>
        AlternateRows,

        /// <summary>Two slots, checkerboard: neighbouring pieces in a row and in the next row differ.</summary>
        AlternateBoards,

        /// <summary>One slot per board code (F01, H01-L …); slots repeat after the palette size.</summary>
        BoardCode,

        /// <summary>Four slots: full board / cut along a hip / straight cut / ridge row.</summary>
        PieceType
    }
}
