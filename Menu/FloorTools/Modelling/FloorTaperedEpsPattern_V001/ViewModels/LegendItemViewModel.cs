namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>One legend row under the preview: colour swatch, meaning, piece count.</summary>
    public sealed class LegendItemViewModel
    {
        public LegendItemViewModel(string colorHex, string label, int count, string typeName)
        {
            ColorHex = colorHex;
            Label = label;
            Count = count;
            TypeName = typeName;
        }

        /// <summary>"#RRGGBB" — turned into a brush by the view's converter.</summary>
        public string ColorHex { get; }
        public string Label { get; }
        public int Count { get; }

        /// <summary>Filled region type the slot will use in Revit.</summary>
        public string TypeName { get; }
    }
}
