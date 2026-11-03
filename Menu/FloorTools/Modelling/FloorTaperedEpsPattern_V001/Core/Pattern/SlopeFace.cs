namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>The part of the floor that drains to outline edge <see cref="EdgeIndex"/>.</summary>
    public sealed class SlopeFace
    {
        public SlopeFace(int edgeIndex, Polygon2D polygon)
        {
            EdgeIndex = edgeIndex;
            Polygon = polygon;
        }

        public int EdgeIndex { get; }
        public Polygon2D Polygon { get; }
    }
}
