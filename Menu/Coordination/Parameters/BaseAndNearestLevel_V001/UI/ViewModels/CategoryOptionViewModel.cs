using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>
    /// One row of the Categories grid (1.1.0): a category found in the loaded elements, how many elements
    /// it holds in each tier, and whether the run covers it. Replaces the three fixed checkboxes of 1.0.0.
    /// </summary>
    public sealed partial class CategoryOptionViewModel : ObservableObject
    {
        /// <summary>Creates a row from one load's counts.</summary>
        public CategoryOptionViewModel(CategoryCount count, bool isSelected)
        {
            Name = count?.Name ?? string.Empty;
            Total = count?.Total ?? 0;
            Tier1 = count?.Tier1 ?? 0;
            Tier2 = count?.Tier2 ?? 0;
            Tier3 = count?.Tier3 ?? 0;
            _isSelected = isSelected;
        }

        /// <summary>Raised when <see cref="IsSelected"/> changes.</summary>
        public event EventHandler SelectionChanged;

        /// <summary>Category name.</summary>
        public string Name { get; }

        /// <summary>Elements of this category in the loaded list.</summary>
        public int Total { get; }

        /// <summary>Of those, how many are Tier 1 (level + offset, re-hostable).</summary>
        public int Tier1 { get; }

        /// <summary>Of those, how many are Tier 2 (offset from host).</summary>
        public int Tier2 { get; }

        /// <summary>Of those, how many are Tier 3 (level only).</summary>
        public int Tier3 { get; }

        /// <summary>True when every element of this category is Tier 1, so all four parameters are written.</summary>
        public bool AllTier1 => Total > 0 && Tier1 == Total;

        /// <summary>"12" or "12 · 9 T1, 3 T2", for the grid's count column.</summary>
        public string CountText
        {
            get
            {
                if (Total == 0) return "—";
                if (AllTier1) return Total.ToString();
                var parts = new System.Collections.Generic.List<string>();
                if (Tier1 > 0) parts.Add($"{Tier1} T1");
                if (Tier2 > 0) parts.Add($"{Tier2} T2");
                if (Tier3 > 0) parts.Add($"{Tier3} T3");
                return $"{Total} · {string.Join(", ", parts)}";
            }
        }

        /// <summary>Tooltip explaining the tier split.</summary>
        public string CountTip => AllTier1
            ? $"{Total} element(s), all with a level and an offset from it. All four parameters are written and they can be re-hosted."
            : $"{Tier1} with a level and an offset from it (all four parameters, re-hostable); " +
              $"{Tier2} whose offset is from the host; {Tier3} with a level only. " +
              "The last two get their level names with no offsets, and are never re-hosted.";

        /// <summary>True when the run covers this category.</summary>
        [ObservableProperty] private bool _isSelected;

        partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
