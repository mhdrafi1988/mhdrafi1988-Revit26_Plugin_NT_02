// =======================================================
// File: ViewConverters.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.UI.Views
// Two small converters this window needs that Shared/Converters.cs does
// not have. Declared in the window's own Resources (UI_UX_PATTERNS.md).
// Replaces V030's ColorStringToBrushConverter (unused there) and
// SlopePercentTextConverter (the slope box now binds to text and is
// validated by the ViewModel).
// =======================================================

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.UI.Views
{
    /// <summary>Non-empty string → Visible, null/empty → Collapsed (inline error lines).</summary>
    public class NonEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }

    /// <summary>Marker colour name → swatch brush, from the same RGB table the run uses.</summary>
    public class ColorNameToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var (r, g, b) = NamedColors.ToRgb(value as string);
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            DependencyProperty.UnsetValue;
    }
}
