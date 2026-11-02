using System.Collections.Generic;
using System.Text;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models
{
    /// <summary>What happened to one view during the run.</summary>
    public enum RenumberOutcome
    {
        /// <summary>Number changed.</summary>
        Renumbered,

        /// <summary>Already held the number the sequence wanted.</summary>
        Unchanged,

        /// <summary>Out of scope, or its detail number is read-only.</summary>
        Skipped,

        /// <summary>Revit refused the write; the reason is in Message.</summary>
        Failed
    }

    /// <summary>One row of the run report.</summary>
    public class RenumberResult
    {
        public string ViewName { get; init; } = string.Empty;
        public string KindLabel { get; init; } = string.Empty;
        public string PreviousNumber { get; init; } = string.Empty;
        public string NewNumber { get; init; } = string.Empty;
        public RenumberOutcome Outcome { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Post-run report: counts for the header tiles, a headline for the shell status
    /// strip (V008 declared SummaryText and never set it), and one row per view.
    /// </summary>
    public class RenumberSummary
    {
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;

        /// <summary>Viewports on the sheet, whatever the filter.</summary>
        public int OnSheet { get; set; }

        /// <summary>Rows the filter put in scope.</summary>
        public int InScope { get; set; }

        public int Renumbered { get; set; }
        public int Unchanged { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }

        public List<RenumberResult> Results { get; } = new();

        /// <summary>Set when the run itself threw rather than one row failing.</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>One line for the shell status strip.</summary>
        public string Headline
        {
            get
            {
                if (!string.IsNullOrEmpty(ErrorMessage))
                    return $"Run failed — {ErrorMessage}";

                if (InScope == 0)
                    return $"{SheetNumber}: nothing in scope — check the view-type filter.";

                var parts = new List<string> { $"{Renumbered} renumbered" };
                if (Unchanged > 0) parts.Add($"{Unchanged} already correct");
                if (Skipped > 0) parts.Add($"{Skipped} skipped");
                if (Failed > 0) parts.Add($"{Failed} failed");

                return $"{SheetNumber}: {string.Join(" · ", parts)} of {InScope} in scope.";
            }
        }

        /// <summary>Full text report, for the clipboard.</summary>
        public string ToReportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Bubble Auto Renumber {BubbleAutoRenumberTool.ShellVersion} — run report");
            sb.AppendLine($"Sheet      : {SheetNumber} — {SheetName}");
            sb.AppendLine($"On sheet   : {OnSheet} viewport(s)");
            sb.AppendLine($"In scope   : {InScope}");
            sb.AppendLine($"Renumbered : {Renumbered}");
            sb.AppendLine($"Unchanged  : {Unchanged}");
            sb.AppendLine($"Skipped    : {Skipped}");
            sb.AppendLine($"Failed     : {Failed}");

            if (!string.IsNullOrEmpty(ErrorMessage))
                sb.AppendLine($"Error      : {ErrorMessage}");

            sb.AppendLine();
            sb.AppendLine("Outcome      Type        Was    ->  Now    View");
            sb.AppendLine(new string('-', 78));

            foreach (RenumberResult r in Results)
            {
                string was = string.IsNullOrEmpty(r.PreviousNumber) ? "(none)" : r.PreviousNumber;
                string now = string.IsNullOrEmpty(r.NewNumber) ? "—" : r.NewNumber;
                sb.AppendLine($"{r.Outcome,-12} {r.KindLabel,-11} {was,-6} ->  {now,-6} {r.ViewName}"
                              + (string.IsNullOrEmpty(r.Message) ? string.Empty : $"   [{r.Message}]"));
            }

            return sb.ToString();
        }
    }
}
