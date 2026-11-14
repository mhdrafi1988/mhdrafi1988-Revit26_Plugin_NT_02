using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>One element row of the grid.</summary>
    public sealed partial class ElementRowViewModel : ObservableObject
    {
        private readonly Action<ElementRowViewModel> _tickChanged;
        private readonly Action<ElementRowViewModel> _choiceChanged;

        /// <summary>Creates a row; <paramref name="tickChanged"/> is called when the user ticks or unticks it.</summary>
        public ElementRowViewModel(long id, string category, string familyAndType, Action<ElementRowViewModel> tickChanged)
            : this(id, category, familyAndType, tickChanged, null)
        {
        }

        /// <summary>
        /// Creates a row; <paramref name="tickChanged"/> is called when the user ticks or unticks it and
        /// <paramref name="choiceChanged"/> when the user changes its "If value exists" choice.
        /// </summary>
        public ElementRowViewModel(long id, string category, string familyAndType,
            Action<ElementRowViewModel> tickChanged, Action<ElementRowViewModel> choiceChanged)
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

        /// <summary>Same list as <see cref="ChoiceOptions"/>, as an instance property for the grid's cell binding.</summary>
        public IReadOnlyList<ExistingChoice> Choices => ChoiceOptions;

        /// <summary>Element id value.</summary>
        public long Id { get; }

        /// <summary>Category name (rows are grouped by it).</summary>
        public string Category { get; }

        /// <summary>"Family: Type".</summary>
        public string FamilyAndType { get; }

        /// <summary>Facts from the last pre-check, or null when the base level has not been read.</summary>
        public ElementFacts Facts { get; private set; }

        /// <summary>When true, setting <see cref="IsChecked"/> does not notify the window (bulk select).</summary>
        internal bool SuppressTickCallback { get; set; }

        /// <summary>When true, setting <see cref="Choice"/> does not notify the window (bulk change).</summary>
        internal bool SuppressChoiceCallback { get; set; }

        [ObservableProperty] private bool _isChecked;
        [ObservableProperty] private string _baseLevel = string.Empty;
        [ObservableProperty] private string _baseOffset = string.Empty;
        [ObservableProperty] private string _currentValue = string.Empty;
        [ObservableProperty] private string _newValue = string.Empty;
        [ObservableProperty] private string _status = "Not read";
        [ObservableProperty] private RowState _state = RowState.Pending;
        [ObservableProperty] private bool _isChanged;
        [ObservableProperty] private bool _isSame;
        [ObservableProperty] private ExistingChoice _choice = ExistingChoice.Default;
        [ObservableProperty] private bool _hasExistingValue;

        /// <summary>True when the row's choice is its own (Skip or Rewrite), not the window default.</summary>
        public bool HasOwnChoice => Choice != ExistingChoice.Default;

        partial void OnChoiceChanged(ExistingChoice value)
        {
            OnPropertyChanged(nameof(HasOwnChoice));
            if (!SuppressChoiceCallback) _choiceChanged?.Invoke(this);
        }

        /// <summary>Which parameters the base level was read from (tooltip on Base Level).</summary>
        public string LevelSource => Facts == null ? "Not read yet"
            : Facts.HasLevel ? "From " + Facts.LevelSource : "No base level parameter found";

        /// <summary>Base offset as a number for sorting (null when not read).</summary>
        public double? OffsetSort => Facts?.HasLevel == true ? Math.Round(Facts.OffsetMm, MidpointRounding.AwayFromZero) : null;

        partial void OnIsCheckedChanged(bool value)
        {
            if (!SuppressTickCallback) _tickChanged?.Invoke(this);
        }

        /// <summary>Shows the facts read by the last pre-check, with the value in <paramref name="format"/>.</summary>
        public void ApplyFacts(ElementFacts facts, ValueFormat format, string customPattern)
        {
            Facts = facts;
            if (facts.HasLevel)
            {
                BaseLevel = facts.LevelName;
                BaseOffset = BaseLevelRules.FormatOffset(facts.OffsetMm);
            }
            else
            {
                BaseLevel = "—";
                BaseOffset = "—";
            }
            CurrentValue = string.IsNullOrEmpty(facts.CurrentValue) ? "—" : facts.CurrentValue;
            RefreshValue(format, customPattern);
            OnPropertyChanged(nameof(OffsetSort));
            OnPropertyChanged(nameof(LevelSource));
        }

        /// <summary>The value in the current format, or null when the base level is not known.</summary>
        public string FormattedValue { get; private set; }

        /// <summary>Recomputes New Value for another format (no Revit call).</summary>
        public void RefreshValue(ValueFormat format, string customPattern)
        {
            FormattedValue = Facts?.HasLevel == true ? ValueFormatter.Format(Facts.LevelName, Facts.OffsetMm, format, customPattern) : null;
            NewValue = FormattedValue ?? "—";
            IsSame = FormattedValue != null && string.Equals(FormattedValue, Facts.CurrentValue, StringComparison.Ordinal);
            IsChanged = FormattedValue != null && !IsSame;
            HasExistingValue = Facts != null && BaseLevelRules.HasDifferentValue(Facts, FormattedValue);
        }

        /// <summary>Records a value written by Run.</summary>
        public void MarkWritten(string value)
        {
            if (Facts != null) Facts.CurrentValue = value;
            CurrentValue = value;
            IsSame = true;
            IsChanged = false;
            HasExistingValue = false;
        }

        /// <summary>Forgets the facts (the element no longer exists).</summary>
        public void MarkMissing()
        {
            Facts = null;
            HasExistingValue = false;
            SetStatus(RowState.Warning, "Skipped · element no longer exists");
        }

        /// <summary>Sets the status cell.</summary>
        public void SetStatus(RowState state, string text)
        {
            State = state;
            Status = text;
        }
    }
}
