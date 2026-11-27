using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>One element row of the grid: its base placement beside its nearest level.</summary>
    public sealed partial class ElementRowViewModel : ObservableObject
    {
        private readonly Action<ElementRowViewModel> _tickChanged;
        private readonly Action<ElementRowViewModel> _choiceChanged;

        /// <summary>
        /// Creates a row. <paramref name="tickChanged"/> is called when the user ticks or unticks it and
        /// <paramref name="choiceChanged"/> when the user changes its "If value exists" choice.
        /// </summary>
        public ElementRowViewModel(long id, string category, string familyAndType,
            Action<ElementRowViewModel> tickChanged, Action<ElementRowViewModel> choiceChanged = null)
        {
            Id = id;
            Category = category;
            FamilyAndType = familyAndType;
            _tickChanged = tickChanged;
            _choiceChanged = choiceChanged;
            _isChecked = true;
        }

        /// <summary>Items of the "If value exists" combo box.</summary>
        public static IReadOnlyList<ExistingChoice> ChoiceOptions { get; } =
            new[] { ExistingChoice.Default, ExistingChoice.Skip, ExistingChoice.Rewrite };

        /// <summary>Same list as <see cref="ChoiceOptions"/>, for the grid's cell binding.</summary>
        public IReadOnlyList<ExistingChoice> Choices => ChoiceOptions;

        /// <summary>Element id value.</summary>
        public long Id { get; }

        /// <summary>Category name: Roofs, Floors or Walls. Rows are grouped by it.</summary>
        public string Category { get; }

        /// <summary>"Family: Type".</summary>
        public string FamilyAndType { get; }

        /// <summary>Facts from the last read, or null before the levels have been read.</summary>
        public ElementFacts Facts { get; private set; }

        /// <summary>When true, setting <see cref="IsChecked"/> does not notify the window (bulk select).</summary>
        internal bool SuppressTickCallback { get; set; }

        /// <summary>When true, setting <see cref="Choice"/> does not notify the window (bulk change).</summary>
        internal bool SuppressChoiceCallback { get; set; }

        [ObservableProperty] private bool _isChecked;
        [ObservableProperty] private string _baseLevel = string.Empty;
        [ObservableProperty] private string _baseOffset = string.Empty;
        [ObservableProperty] private string _elevation = string.Empty;
        [ObservableProperty] private string _nearestLevel = string.Empty;
        [ObservableProperty] private string _nearestOffset = string.Empty;
        [ObservableProperty] private string _status = "Not read";
        [ObservableProperty] private RowState _state = RowState.Pending;
        [ObservableProperty] private bool _isChanged;
        [ObservableProperty] private bool _hasExistingValue;
        [ObservableProperty] private ExistingChoice _choice = ExistingChoice.Default;
        [ObservableProperty] private string _flagTip = string.Empty;

        /// <summary>True when the re-host action would skip this element, so the grid shows its marker.</summary>
        public bool HasFlag => !string.IsNullOrEmpty(FlagTip);

        /// <summary>"T1", "T2", "T3" or "—", for the grid's Tier column (1.1.0).</summary>
        public string TierLabel => Facts == null ? "—" : Facts.Tier switch
        {
            Core.LevelTier.LevelAndOffset => "T1",
            Core.LevelTier.HostOffset => "T2",
            Core.LevelTier.ScheduleOnly => "T3",
            _ => "—"
        };

        /// <summary>Which parameter pair the base placement came from, for the row tooltip.</summary>
        public string SourceTip => Facts == null || !Facts.HasBase ? string.Empty : Facts.Source;

        partial void OnIsCheckedChanged(bool value)
        {
            if (!SuppressTickCallback) _tickChanged?.Invoke(this);
        }

        partial void OnChoiceChanged(ExistingChoice value)
        {
            if (!SuppressChoiceCallback) _choiceChanged?.Invoke(this);
        }

        partial void OnFlagTipChanged(string value) => OnPropertyChanged(nameof(HasFlag));

        /// <summary>Puts the row back to "not read", keeping its tick and its choice.</summary>
        public void Reset()
        {
            Facts = null;
            BaseLevel = string.Empty;
            BaseOffset = string.Empty;
            Elevation = string.Empty;
            NearestLevel = string.Empty;
            NearestOffset = string.Empty;
            Status = "Not read";
            State = RowState.Pending;
            IsChanged = false;
            HasExistingValue = false;
            FlagTip = string.Empty;
            OnPropertyChanged(nameof(TierLabel));
        }

        /// <summary>Fills the row from <paramref name="facts"/> and <paramref name="verdict"/>.</summary>
        public void Apply(ElementFacts facts, RowVerdict verdict)
        {
            Facts = facts;
            if (facts == null)
            {
                Reset();
                return;
            }

            BaseLevel = facts.HasBase ? facts.BaseLevelName : "—";
            NearestLevel = facts.NearestFound ? facts.NearestLevelName : "—";
            // Tier 2 and 3 have no offset measured from the level, so the three numeric columns read "n/a"
            // rather than showing a number the tool cannot justify.
            BaseOffset = facts.HasBase && facts.OffsetKnown ? ValueText.Offset(facts.BaseOffsetMm) : facts.HasBase ? "n/a" : "—";
            Elevation = facts.HasBase && facts.OffsetKnown ? ValueText.Offset(facts.ElevationMm).TrimStart('+') : facts.HasBase ? "n/a" : "—";
            NearestOffset = facts.NearestFound && facts.OffsetKnown ? ValueText.Offset(facts.NearestOffsetMm) : facts.NearestFound ? "n/a" : "—";
            IsChanged = facts.IsChanged;
            HasExistingValue = facts.AnyDifferent;
            State = verdict.State;
            Status = verdict.Text;

            RehostSkip block = facts.RehostBlock;
            FlagTip = block is RehostSkip.None or RehostSkip.NoChange
                ? string.Empty
                : "Re-host will skip this element: " + BaseAndNearestRules.RehostSkipText(block);
            OnPropertyChanged(nameof(SourceTip));
            OnPropertyChanged(nameof(TierLabel));
        }

        /// <summary>Shows the outcome of a run on this row.</summary>
        public void ApplyResult(RunRowResult result)
        {
            if (result == null) return;
            State = result.State;
            Status = result.Text;
        }
    }
}
