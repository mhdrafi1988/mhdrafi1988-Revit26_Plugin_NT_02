using System.Globalization;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>Summary grid row: Layer | Lines | Total length m.</summary>
    public sealed class SummaryRowViewModel
    {
        public SummaryRowViewModel(string layer, int lines, double totalLengthM)
        {
            Layer = layer;
            Lines = lines;
            TotalLength = totalLengthM.ToString("0.00", CultureInfo.CurrentCulture);
        }

        public string Layer { get; }
        public int Lines { get; }
        public string TotalLength { get; }
    }
}
