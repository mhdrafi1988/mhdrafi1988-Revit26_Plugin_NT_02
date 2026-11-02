using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.ViewModels;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.Views
{
    /// <summary>Shows the per-row Select / Show in 3D buttons on Category and Type rows (not Workset rows).</summary>
    public class KindToTypeVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ElementTreeNodeKind.Type or ElementTreeNodeKind.Category ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
