using Revit26_Plugin_Revised.SectionsToSheet.V001.UI.ViewModels;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.UI.Converters
{
    /// <summary>
    /// Two-way enum ↔ radio button: checked when the value's name equals ConverterParameter;
    /// checking the button sets the value. Unchecking does nothing (another button is checked).
    /// </summary>
    public sealed class EnumMatchConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true && parameter != null && targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()) : Binding.DoNothing;
    }

    /// <summary>Row chip colours: ConverterParameter "Background" (default) or "Foreground".</summary>
    public sealed class ChipKindBrushConverter : IValueConverter
    {
        private static readonly Brush NeutralBg = Frozen(0xEC, 0xEF, 0xF3), NeutralFg = Frozen(0x4A, 0x5F, 0x73);
        private static readonly Brush SheetBg = Frozen(0xFD, 0xF1, 0xE1), SheetFg = Frozen(0x7A, 0x46, 0x00);
        private static readonly Brush LockBg = Frozen(0xFB, 0xE8, 0xE7), LockFg = Frozen(0x8E, 0x2A, 0x26);

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool fg = string.Equals(parameter as string, "Foreground", StringComparison.OrdinalIgnoreCase);
            return value switch
            {
                ChipKind.OnSheet => fg ? SheetFg : SheetBg,
                ChipKind.Locked => fg ? LockFg : LockBg,
                _ => fg ? NeutralFg : NeutralBg
            };
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

        internal static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Row status line colour.</summary>
    public sealed class RowStatusBrushConverter : IValueConverter
    {
        private static readonly Brush Placed = ChipKindBrushConverter.Frozen(0x2E, 0x7D, 0x4F);
        private static readonly Brush Moved = ChipKindBrushConverter.Frozen(0x9A, 0x5B, 0x00);
        private static readonly Brush Warning = ChipKindBrushConverter.Frozen(0x5B, 0x71, 0x85);
        private static readonly Brush Problem = ChipKindBrushConverter.Frozen(0xB2, 0x3A, 0x35);

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            RowStatusKind.Placed => Placed,
            RowStatusKind.Moved => Moved,
            RowStatusKind.Problem => Problem,
            _ => Warning
        };

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
