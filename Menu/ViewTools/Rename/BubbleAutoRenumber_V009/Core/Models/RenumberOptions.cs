namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models
{
    /// <summary>
    /// Everything the user chose in the window, as plain data.
    /// </summary>
    public class RenumberOptions
    {
        /// <summary>First position of the sequence. 1-based, always &gt; 0. In letters,
        /// 1 is "A" and 27 is "AA" — see SequenceLabel.</summary>
        public int StartNumber { get; set; } = 1;

        /// <summary>Numbers (1, 2, 3) or letters (A, B, C). NEW in 9.3.0; the default is
        /// Numbers, which is what every earlier version wrote.</summary>
        public NumberingStyle Style { get; set; } = NumberingStyle.Numbers;

        /// <summary>The first value of the sequence as it will be written: "1" or "A".</summary>
        public string StartLabel => Services.SequenceLabel.Format(StartNumber, Style);

        /// <summary>Row-banding tolerance in internal units (feet), converted from the
        /// window's millimetres by the ViewModel.</summary>
        public double ThresholdFt { get; set; }

        // ── Type filter (NEW in V009) ──────────────────────────────────────────
        public bool IncludeSections { get; set; } = true;
        public bool IncludeDrafting { get; set; } = true;
        public bool IncludeDetail { get; set; } = true;
        public bool IncludeElevations { get; set; } = true;

        /// <summary>
        /// Master toggle: take every viewport that has a detail number at all,
        /// ignoring the four type flags above (plans, 3D views, legends included).
        /// </summary>
        public bool AnyNumberedView { get; set; }

        /// <summary>True when this row is in scope for the current filter.</summary>
        public bool Includes(BubbleRow row)
        {
            if (row == null || !row.HasNumberParameter) return false;
            if (AnyNumberedView) return true;

            return row.Kind switch
            {
                BubbleViewKind.Section => IncludeSections,
                BubbleViewKind.Drafting => IncludeDrafting,
                BubbleViewKind.Detail => IncludeDetail,
                BubbleViewKind.Elevation => IncludeElevations,
                _ => false
            };
        }

        /// <summary>True when at least one type is selected — otherwise there is nothing to do.</summary>
        public bool HasAnyTypeSelected =>
            AnyNumberedView || IncludeSections || IncludeDrafting || IncludeDetail || IncludeElevations;
    }
}
