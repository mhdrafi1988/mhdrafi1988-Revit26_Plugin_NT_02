using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>One element row of the grid.</summary>
    public sealed partial class ElementRowViewModel : ObservableObject
    {
        private readonly Action<ElementRowViewModel> _tickChanged;

        /// <summary>Creates a row; <paramref name="tickChanged"/> is called when the user ticks or unticks it.</summary>
        public ElementRowViewModel(long id, string category, string familyAndType, Action<ElementRowViewModel> tickChanged)
        {
            Id = id;
            Category = category;
            FamilyAndType = familyAndType;
            _tickChanged = tickChanged;
            _isChecked = true;
        }

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

        [ObservableProperty] private bool _isChecked;
        [ObservableProperty] private string _baseLevel = string.Empty;
        [ObservableProperty] private string _baseOffset = string.Empty;
        [ObservableProperty] private string _currentValue = string.Empty;
        [ObservableProperty] private string _newValue = string.Empty;
        [ObservableProperty] private string _status = "Not read";
        [ObservableProperty] private RowState _state = RowState.Pending;

        /// <summary>Base offset as a number for sorting (null when not read).</summary>
        public double? OffsetSort => Facts?.HasLevel == true ? Math.Round(Facts.OffsetMm, MidpointRounding.AwayFromZero) : null;

        partial void OnIsCheckedChanged(bool value)
        {
            if (!SuppressTickCallback) _tickChanged?.Invoke(this);
        }

        /// <summary>Shows the facts read by the last pre-check.</summary>
        public void ApplyFacts(ElementFacts facts)
        {
            Facts = facts;
            if (facts.HasLevel)
            {
                BaseLevel = facts.LevelName;
                BaseOffset = BaseLevelRules.FormatOffset(facts.OffsetMm);
                NewValue = BaseLevelRules.FormatValue(facts.LevelName, facts.OffsetMm);
            }
            else
            {
                BaseLevel = "—";
                BaseOffset = "—";
                NewValue = "—";
            }
            CurrentValue = string.IsNullOrEmpty(facts.CurrentValue) ? "—" : facts.CurrentValue;
            OnPropertyChanged(nameof(OffsetSort));
        }

        /// <summary>Forgets the facts (the element no longer exists).</summary>
        public void MarkMissing()
        {
            Facts = null;
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
