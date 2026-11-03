namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// The 4 view-aligned bounding-box sides used to bucket floor boundary edges.
    /// "North" = top of the active view (view rotation), NOT Project/True North.
    /// </summary>
    public enum EdgeDirection
    {
        North,
        South,
        East,
        West
    }
}
