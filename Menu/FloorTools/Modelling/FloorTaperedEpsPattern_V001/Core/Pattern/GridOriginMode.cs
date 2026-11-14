namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    public enum GridOriginMode
    {
        /// <summary>Grid starts at the minimum corner of the outline's bounding box in the grid frame.</summary>
        BoundingBoxCorner,

        /// <summary>Grid is centred so the cut pieces on opposite sides are equal.</summary>
        Centred
    }
}
