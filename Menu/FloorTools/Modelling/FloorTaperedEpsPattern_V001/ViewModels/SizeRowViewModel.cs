using System.Globalization;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>One row of the board size comparison grid.</summary>
    public sealed class SizeRowViewModel
    {
        public SizeRowViewModel(SizeComparisonRow row)
        {
            Size = row.Size;
            CultureInfo ci = CultureInfo.CurrentCulture;
            SizeText = string.Format(ci, "{0:0.00} × {1:0.00}", row.Size.LengthM, row.Size.WidthM);
            Pieces = row.Pieces;
            Full = row.Full;
            FullShare = row.Pieces > 0 ? row.FullAreaShare.ToString("P0", ci) : "—";
            HipCuts = row.HipCuts;
            StraightCuts = row.StraightCuts;
            Codes = row.Codes;
            Slivers = row.Slivers;
            Best = row.IsBest ? "★" : string.Empty;
            Note = row.Note;
        }

        public BoardSize Size { get; }
        public string SizeText { get; }
        public int Pieces { get; }
        public int Full { get; }
        public string FullShare { get; }
        public int HipCuts { get; }
        public int StraightCuts { get; }
        public int Codes { get; }
        public int Slivers { get; }
        public string Best { get; }
        public string Note { get; }
    }
}
