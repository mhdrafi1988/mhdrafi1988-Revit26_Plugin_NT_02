using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.UI.Converters
{
    /// <summary>A step's state text ("active", "done"…) to the height of its row: the open step fills the space.</summary>
    public sealed class StepRowHeightConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value as string == "active" ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Visible when the bound text is one of the comma-separated values in the parameter.</summary>
    public sealed class StateIsConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value?.ToString() ?? string.Empty;
            var wanted = (parameter as string ?? string.Empty).Split(',', '|').Select(s => s.Trim());
            return wanted.Contains(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.UI.Converters
{
    /// <summary>What a step's number circle shows: ✓ when done, – when skipped, else the step number (the parameter).</summary>
    public sealed class StepGlyphConverter : System.Windows.Data.IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => (value as string) switch
            {
                "done" or "locked" => "✓",
                "skipped" => "–",
                _ => parameter?.ToString() ?? string.Empty
            };

        /// <inheritdoc/>
        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
