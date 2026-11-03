// =======================================================
// File: ColorStringToBrushConverter.cs          ← filename typo fixed (.cs.cs → .cs)
// Fixes:
//   #4  Removed duplicate ColorStringToBrushConverter_Classic
//       (identical body, different class name — keep one).
//   #4  Corrected file extension typo (.cs.cs → .cs).
// =======================================================

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.UI.Views
{
    public class ColorStringToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorString)
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString)); }
                catch { return Brushes.Black; }
            }
            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
