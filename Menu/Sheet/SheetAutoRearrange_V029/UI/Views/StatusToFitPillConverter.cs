using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.UI.Views
{
    /// <summary>
    /// Tool-local converter (not merged to shared Converters.cs) mapping
    /// ViewFitStatus to the Fit column's display text. Mirrors
    /// RoofEdgeSections' StatusToEnabledConverter pattern — instantiated
    /// only in this Window's own Resources block.
    /// </summary>
    public class StatusToFitTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ViewFitStatus status
                ? status switch
                {
                    ViewFitStatus.Fits => "OK",
                    ViewFitStatus.Overflow => "Overflow",
                    ViewFitStatus.NotSelected => "Not Selected",
                    _ => "—"
                }
                : "—";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Maps ViewFitStatus to the pill's brush — green/red/muted, matching SharedStyles status colors.</summary>
    public class StatusToFitBrushConverter : IValueConverter
    {
        private readonly SolidColorBrush _ok = new(Color.FromRgb(0x2E, 0x9E, 0x64));       // ColorSuccess
        private readonly SolidColorBrush _overflow = new(Color.FromRgb(0xD9, 0x53, 0x4F)); // ColorDanger
        private readonly SolidColorBrush _muted = new(Color.FromRgb(0x8F, 0xA3, 0xB8));    // TextTertiary

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ViewFitStatus status
                ? status switch
                {
                    ViewFitStatus.Fits => _ok,
                    ViewFitStatus.Overflow => _overflow,
                    _ => _muted
                }
                : _muted;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// V008 NEW: collapses an element when the bound string is null/empty —
    /// used to hide the copiable log-path TextBox until a log has actually
    /// been saved once.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && !string.IsNullOrWhiteSpace(s)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// V014 NEW: generic enum-to-bool comparer — "boundValue ==
    /// (the enum value named in ConverterParameter)", one-way only.
    /// V022 NOTE: no longer used by this file's Row Fill Strategy picker —
    /// superseded there by StrategySelectedBrushConverter, since
    /// ConverterParameter can't be data-bound to each button's own Tag
    /// (see that converter's remarks). Left declared/registered here since
    /// it's still a generically useful single-parameter enum comparer if
    /// a future ConverterParameter can be a literal (non-bound) value.
    /// </summary>
    public class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string? valueName = value.ToString();
            string? paramName = parameter.ToString();
            return string.Equals(valueName, paramName, StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException("One-way only — see StrategyButton_Click in SheetAutoRearrangeWindow.xaml.cs for the actual strategy change.");
    }

    /// <summary>
    /// V022 NEW (revised). Drives the Row Fill Strategy segmented picker's
    /// "selected" visual directly via per-instance MultiBinding on each
    /// Button (Background/BorderBrush/Foreground), rather than a
    /// Style.Triggers MultiDataTrigger — Condition.Binding inside
    /// Style.Triggers does not reliably resolve RelativeSource Self/
    /// AncestorType against the styled element itself (that lookup is
    /// template-context-dependent, not guaranteed for a plain Style
    /// applied to a non-templated-content control), which was causing the
    /// whole ResourceDictionary/Window.Resources merge to behave
    /// unpredictably at load — including, apparently, breaking the
    /// containing Expander's own expand/collapse. Per-instance binding
    /// (each Button's own MultiBinding, with a literal Binding Path="Tag"
    /// resolved against that same Button as DataContext-sibling via
    /// ElementName, see XAML) sidesteps the ambiguity entirely.
    /// Takes (RowFillStrategy, Tag) same as before; returns the matching
    /// Brush directly instead of a bool, since Setter Value can't easily
    /// swap 3 properties from one bool without either 3 separate
    /// converters or a style — simplest to return the right value per
    /// binding, one converter instance per property.
    /// </summary>
    public class StrategySelectedBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isSelected = values.Length >= 2 && values[0] != null && values[1] != null
                && string.Equals(values[0].ToString(), values[1].ToString(), StringComparison.Ordinal);

            // parameter selects which property this call is for: "Background", "Border", or "Foreground" —
            // kept as 3 distinct modes (not just "Navy"/"White") since unselected Background (white) and
            // unselected BorderBrush (subtle grey) must differ, even though both use navy when selected.
            string mode = parameter as string ?? "Background";

            return mode switch
            {
                "Foreground" => isSelected
                    ? System.Windows.Media.Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0x5B, 0x71, 0x85)),  // TextSecondary

                "Border" => isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F))   // NavyPrimary
                    : new SolidColorBrush(Color.FromRgb(0xD7, 0xE1, 0xED)), // fallback neutral border (BrushBorderSubtle not present in SharedStyles — see prior flag)

                _ => isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F))   // NavyPrimary
                    : System.Windows.Media.Brushes.White
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException("One-way only — see StrategyButton_Click in SheetAutoRearrangeWindow.xaml.cs for the actual strategy change.");
    }

    /// <summary>
    /// V029: shows an enum value as words in a ComboBox — "EdgeToEdge" reads "Edge to edge",
    /// "EqualSpacing" reads "Equal spacing". One-way only; the ComboBox still selects the enum value.
    /// </summary>
    public class EnumWordsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = value?.ToString() ?? string.Empty;
            var words = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    words.Append(' ').Append(char.ToLowerInvariant(name[i]));
                else
                    words.Append(name[i]);
            }
            return words.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            System.Windows.Data.Binding.DoNothing;
    }
}
