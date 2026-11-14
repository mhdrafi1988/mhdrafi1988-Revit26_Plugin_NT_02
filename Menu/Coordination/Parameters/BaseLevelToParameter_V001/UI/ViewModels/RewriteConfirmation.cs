using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>The user's answer in the "Rewrite existing values?" dialog.</summary>
    public enum RewriteAnswer
    {
        /// <summary>Do not run.</summary>
        Cancel,
        /// <summary>Run and replace the existing values.</summary>
        Rewrite,
        /// <summary>Run, but keep the existing values of these elements.</summary>
        Skip
    }

    /// <summary>One element in the "Rewrite existing values?" dialog.</summary>
    public sealed class RewriteRow
    {
        /// <summary>New-value text for an element whose old value "Clear old values" empties.</summary>
        public const string Cleared = "(cleared)";

        /// <summary>Creates a row.</summary>
        public RewriteRow(long id, string currentValue, string newValue)
        {
            Id = id;
            CurrentValue = currentValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
        }

        /// <summary>Element id value.</summary>
        public long Id { get; }

        /// <summary>Value the element has now.</summary>
        public string CurrentValue { get; }

        /// <summary>Value Run would write.</summary>
        public string NewValue { get; }
    }

    /// <summary>What the "Rewrite existing values?" dialog shows before Run replaces values.</summary>
    public sealed class RewriteConfirmation
    {
        /// <summary>Most rows listed in the dialog; the full list is logged after the run.</summary>
        public const int MaxRows = 10;

        /// <summary>Target parameter name.</summary>
        public string TargetName { get; set; } = string.Empty;

        /// <summary>Elements whose existing value Run would replace.</summary>
        public int Count { get; set; }

        /// <summary>Elements Run would write in total (rewrites included).</summary>
        public int Total { get; set; }

        /// <summary>Up to <see cref="MaxRows"/> of the elements to rewrite.</summary>
        public List<RewriteRow> Rows { get; set; } = new();

        /// <summary>Lead sentence of the dialog.</summary>
        public string Message =>
            $"{Count} of the {Total} elements Run will write already have a different value in “{TargetName}”; " +
            "Run replaces it (or clears it where there is no base level).";

        /// <summary>Hint under the list.</summary>
        public string Hint =>
            (Count > Rows.Count ? $"The first {Rows.Count} are listed. " : string.Empty) +
            "Every rewritten value is logged (old → new). Ctrl+Z in Revit undoes the whole run.";

        /// <summary>Text of the Rewrite button.</summary>
        public string RewriteText => $"Rewrite {Count}";

        /// <summary>Text of the Skip button.</summary>
        public string SkipText => $"Skip these, write {Total - Count}";
    }
}
