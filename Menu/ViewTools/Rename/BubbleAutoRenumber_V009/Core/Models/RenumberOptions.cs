namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models
{
    /// <summary>
    /// Everything the user chose in the window, as plain data.
    /// </summary>
    public class RenumberOptions
    {
        /// <summary>First number of the sequence. 1-based, always &gt; 0.</summary>
        public int StartNumber { get; set; } = 1;

        /// <summary>How labels are written (1, 01, A or a). Default keeps the old plain numbers.</summary>
        public NumberStyle Style { get; set; } = NumberStyle.Numeric;

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
