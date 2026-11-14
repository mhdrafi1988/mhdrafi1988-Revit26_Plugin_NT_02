// =======================================================
// File: ViewConverters.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.Views
// New in V013: two small converters Shared/Converters.cs does not have.
// Declared in the window's own Resources (UI_UX_PATTERNS.md).
// =======================================================

using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.Views
{
    /// <summary>Non-empty string → Visible, null/empty → Collapsed (inline error lines).</summary>
    public class NonEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }

    /// <summary>Marker colour name → swatch brush, from the same table the run uses.</summary>
    public class ColorNameToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var c = NamedColorHelper.ToRevitColor(value as string);
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue));
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }
}
