namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>A text label to place at <see cref="Position"/> (board centroid).</summary>
    public sealed class PatternLabel
    {
        public PatternLabel(Vec2 position, string text, string boardId, int? edgeIndex = null)
        {
            Position = position;
            Text = text;
            BoardId = boardId;
            EdgeIndex = edgeIndex;
        }

        /// <summary>Side (floor edge) of the board, for group-per-side output.</summary>
        public int? EdgeIndex { get; }

        public Vec2 Position { get; }
        public string Text { get; }
        public string BoardId { get; }
    }
}
