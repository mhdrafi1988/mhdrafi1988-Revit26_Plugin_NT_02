using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.ViewModels
{
    /// <summary>A numeric text input with range validation. <see cref="Value"/> is null while the text is invalid.</summary>
    public sealed partial class NumericFieldViewModel : ObservableObject
    {
        private readonly string _format;

        public NumericFieldViewModel(string label, string unit, double min, double max, double defaultValue, string format)
        {
            Label = label;
            Unit = unit;
            Min = min;
            Max = max;
            Default = defaultValue;
            _format = format;
            Text = Format(defaultValue);
        }

        public string Label { get; }
        public string Unit { get; }
        public double Min { get; }
        public double Max { get; }
        public double Default { get; }

        public string RangeHint => $"{Format(Min)}–{Format(Max)} {Unit}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string? _error;

        [ObservableProperty]
        private string _text = string.Empty;

        public double? Value { get; private set; }

        public bool HasError => Error != null;

        public event EventHandler? ValueChanged;

        public void Reset() => Text = Format(Default);

        partial void OnTextChanged(string value)
        {
            if (TryParse(value, out double parsed))
            {
                if (parsed < Min - 1e-9 || parsed > Max + 1e-9)
                {
                    Value = null;
                    Error = $"{Label} must be {RangeHint}.";
                }
                else
                {
                    Value = parsed;
                    Error = null;
                }
            }
            else
            {
                Value = null;
                Error = $"{Label}: enter a number ({RangeHint}).";
            }

            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private string Format(double v) => v.ToString(_format, CultureInfo.CurrentCulture);

        private static bool TryParse(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
