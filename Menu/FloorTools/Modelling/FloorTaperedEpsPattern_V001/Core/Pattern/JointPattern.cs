namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>How the cross joints of one row relate to the rows above it on the same side (contour-rows mode).</summary>
    public enum JointPattern
    {
        /// <summary>Every row set out on its own by the Row start option (V004 behaviour).</summary>
        AsSetOut,

        /// <summary>The first row's joints are carried straight up the slope: joints line up in every row.</summary>
        Aligned,

        /// <summary>Like Aligned, but every second row is shifted by half a board (brick bond).</summary>
        StaggerHalf,

        /// <summary>Like Aligned, but rows are shifted by a third of a board in turn (0, ⅓, ⅔, 0 …).</summary>
        StaggerThird
    }
}
