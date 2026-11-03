using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Converters
{
    /// <summary>
    /// Shows an element only when the bound integer count is zero — used to display the
    /// per-group "No … openings" placeholder in the grouped drainage-seed expanders.
    /// Returns <see cref="Visibility.Visible"/> when the value is 0, otherwise
    /// <see cref="Visibility.Collapsed"/>. This is the inverse of the shared
    /// <c>IntToVisibilityConverter</c>, which hides on zero.
    /// </summary>
    public class ZeroToVisibilityConverter : IValueConverter
    {
        /// <summary>Converts an integer count to a visibility (visible only when 0).</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is int i && i == 0) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Not supported; one-way binding only.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }
}
