// =======================================================
// File: DrainResultRow.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.ViewModels
// New in V013: one row of the results side panel. Column names match the
// Excel "Vertex Data" sheet, so the panel and the workbook read the same.
// Display strings are formatted once here; the DataGrid binds to them.
// Longest 10 shows only PathLength_m, SlopePercent and ElevCalc_mm (as asked);
// ElevModel / ElevDiff stay in the Excel workbook and the read-back warning.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using System.Globalization;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.ViewModels
{
    public enum ResultsTab
    {
        Longest,
        Skipped
    }

    public sealed class DrainResultRow
    {
        public int RowNumber { get; init; }
        public int VertexIndex { get; init; }
        public string PathLengthText { get; init; }
        public string SlopePercentText { get; init; }
        public string ElevCalcText { get; init; }
        public bool IsTop { get; init; }
        public string Status { get; init; }
        public string Fix { get; init; }
        public XYZ Position { get; init; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static DrainResultRow FromProcessed(DrainVertexData v, int rowNumber, double slopePercent)
        {
            return new DrainResultRow
            {
                RowNumber = rowNumber,
                VertexIndex = v.VertexIndex,
                PathLengthText = v.PathLengthMeters.ToString("0.00", Inv),
                SlopePercentText = slopePercent.ToString("0.0##", Inv),
                ElevCalcText = v.ElevationOffsetMm.ToString("0", Inv),
                IsTop = rowNumber == 1,
                Position = v.Position
            };
        }

        public static DrainResultRow FromSkipped(DrainVertexData v, int rowNumber)
        {
            bool noPath = v.SkipReason != "Beyond distance";
            return new DrainResultRow
            {
                RowNumber = rowNumber,
                VertexIndex = v.VertexIndex,
                Status = noPath ? "No path" : "Beyond distance",
                PathLengthText = noPath ? "—" : v.PathLengthMeters.ToString("0.00", Inv),
                Fix = noPath ? "Check openings or raise the max edge distance" : "Raise the max path distance",
                Position = v.Position
            };
        }

        public static string Header(ResultsTab tab) => tab == ResultsTab.Longest
            ? "#\tPathLength_m\tSlopePercent\tElevCalc_mm"
            : "#\tVertexIndex\tStatus\tPathLength_m\tSuggested fix";

        /// <summary>Tab-separated, so a paste into Excel lands in columns.</summary>
        public string ToTsv(ResultsTab tab) => tab == ResultsTab.Longest
            ? $"{RowNumber}\t{PathLengthText}\t{SlopePercentText}\t{ElevCalcText}"
            : $"{RowNumber}\t{VertexIndex}\t{Status}\t{PathLengthText}\t{Fix}";
    }
}
