// =======================================================
// File: SlopeRowViewModel.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Purpose: One of the 4 fixed rows in the Multi-Slope Floor Variants
//          section — a checkbox (IsEnabled) plus a typable/searchable
//          slope % ComboBox (PercentText). Raises RowChanged whenever
//          either changes, so the parent ViewModel can recompute its
//          live metrics (Active Slopes / Copies to Create / Worksets New).
// =======================================================

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.UI.ViewModels
{
    public partial class SlopeRowViewModel : ObservableObject
    {
        public int RowIndex { get; }
        public string RowLabel => $"Slope {RowIndex}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRowActive))]
        private bool isEnabled;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRowActive))]
        [NotifyPropertyChangedFor(nameof(Percent))]
        private string percentText = "1";

        /// <summary>Parsed slope percentage. 0 when PercentText is not a valid positive number.</summary>
        public double Percent =>
            double.TryParse(PercentText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
                ? value
                : 0;

        /// <summary>Row will produce a floor copy: checked AND a valid positive percent typed.</summary>
        public bool IsRowActive => IsEnabled && Percent > 0;

        public event Action Changed;

        public SlopeRowViewModel(int rowIndex)
        {
            RowIndex = rowIndex;
        }

        partial void OnIsEnabledChanged(bool value) => Changed?.Invoke();
        partial void OnPercentTextChanged(string value) => Changed?.Invoke();
    }
}
