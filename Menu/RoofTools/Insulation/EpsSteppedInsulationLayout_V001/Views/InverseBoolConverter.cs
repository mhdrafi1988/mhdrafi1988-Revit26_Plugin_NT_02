using System;
using System.Globalization;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Views
{
    /// <summary>Inverts a boolean for two-way binding (e.g. radio button pairs).</summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : value;
    }
}
