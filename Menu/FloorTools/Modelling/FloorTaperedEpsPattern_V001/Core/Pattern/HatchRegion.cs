namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>One filled region to draw: the piece outline and its colour slot.</summary>
    public sealed class HatchRegion
    {
        public HatchRegion(Polygon2D polygon, int slot, int? edgeIndex, string boardId)
        {
            Polygon = polygon;
            Slot = slot;
            EdgeIndex = edgeIndex;
            BoardId = boardId;
        }

        public Polygon2D Polygon { get; }
        public int Slot { get; }
        public int? EdgeIndex { get; }
        public string BoardId { get; }
    }
}
