namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Legend row: colour slot, what it means, how many pieces.</summary>
    public sealed class LegendEntry
    {
        public LegendEntry(int slot, string label, int count)
        {
            Slot = slot;
            Label = label;
            Count = count;
        }

        public int Slot { get; }
        public string Label { get; }
        public int Count { get; }
        public HatchColor DefaultColor => HatchPalette.ForSlot(Slot);
    }
}
