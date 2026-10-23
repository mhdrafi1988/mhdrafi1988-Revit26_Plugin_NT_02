using System;
using System.Globalization;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.UI.Views
{
    /// <summary>True when the first two bound values are equal strings; used to highlight the active count tile.</summary>
    public sealed class EqualsMultiConverter : IMultiValueConverter
    {
        /// <summary>Compares values[0] and values[1] as strings.</summary>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values != null && values.Length >= 2 &&
            string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(values[0]?.ToString());

        /// <summary>Not supported (one-way only).</summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
