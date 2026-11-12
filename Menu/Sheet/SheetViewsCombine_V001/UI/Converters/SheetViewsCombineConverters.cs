using Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.Converters
{
    /// <summary>The sheet colour code shared by the grid, the plan and the preview.</summary>
    public static class SheetColors
    {
        /// <summary>Sheet 1 (blue), Sheet 2 (amber), target (navy): strong colour.</summary>
        public static Brush Strong(int slot) => slot switch { 1 => S1, 2 => S2, _ => S3 };

        /// <summary>Same, soft background.</summary>
        public static Brush Soft(int slot) => slot switch { 1 => S1Soft, 2 => S2Soft, _ => S3Soft };

        private static readonly Brush S1 = Frozen(0x1F, 0x5F, 0xBF);
        private static readonly Brush S1Soft = Frozen(0xE6, 0xEE, 0xFB);
        private static readonly Brush S2 = Frozen(0x9A, 0x5B, 0x00);
        private static readonly Brush S2Soft = Frozen(0xFD, 0xF1, 0xE1);
        private static readonly Brush S3 = Frozen(0x1E, 0x3A, 0x5F);
        private static readonly Brush S3Soft = Frozen(0xEC, 0xEF, 0xF3);

        /// <summary>A frozen solid brush.</summary>
        public static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>The slot in a value: an int, or a group key "slot|text".</summary>
        public static int SlotOf(object value) => value switch
        {
            int i => i,
            string s when s.Length == 1 && char.IsDigit(s[0]) => s[0] - '0',
            string s when s.Length > 1 && s[1] == '|' && char.IsDigit(s[0]) => s[0] - '0',
            _ => 3
        };
    }

    /// <summary>Slot (int or "slot|text" key) → sheet brush. Parameter "Soft" gives the background shade.</summary>
    public sealed class SlotBrushConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int slot = SheetColors.SlotOf(value);
            return parameter as string == "Soft" ? SheetColors.Soft(slot) : SheetColors.Strong(slot);
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>"slot|text" → "text" (group headers).</summary>
    public sealed class AfterPipeConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value?.ToString() ?? string.Empty;
            int i = s.IndexOf('|');
            return i >= 0 ? s[(i + 1)..] : s;
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary><see cref="RowStatusKind"/> → text brush; parameter "Soft" gives the pill background.</summary>
    public sealed class StatusKindBrushConverter : IValueConverter
    {
        private static readonly Brush Grey = SheetColors.Frozen(0x5B, 0x71, 0x85);
        private static readonly Brush GreySoft = SheetColors.Frozen(0xEC, 0xEF, 0xF3);
        private static readonly Brush Green = SheetColors.Frozen(0x2E, 0x7D, 0x4F);
        private static readonly Brush GreenSoft = SheetColors.Frozen(0xE6, 0xF4, 0xEC);
        private static readonly Brush Blue = SheetColors.Frozen(0x1F, 0x5F, 0xBF);
        private static readonly Brush BlueSoft = SheetColors.Frozen(0xE6, 0xEE, 0xFB);
        private static readonly Brush Navy = SheetColors.Frozen(0x1E, 0x3A, 0x5F);
        private static readonly Brush Red = SheetColors.Frozen(0xB2, 0x3A, 0x35);
        private static readonly Brush RedSoft = SheetColors.Frozen(0xFB, 0xE8, 0xE7);
        private static readonly Brush None = Brushes.Transparent;

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool soft = parameter as string == "Soft";
            return value is RowStatusKind kind ? kind switch
            {
                RowStatusKind.Repack => soft ? GreenSoft : Green,
                RowStatusKind.Move => soft ? BlueSoft : Blue,
                RowStatusKind.Copy => soft ? GreySoft : Navy,
                RowStatusKind.Skip => soft ? RedSoft : Red,
                RowStatusKind.Stay => soft ? GreySoft : Grey,
                _ => soft ? None : Grey
            } : (soft ? None : Grey);
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Enum ↔ radio button: true when the value's name equals the parameter.</summary>
    public sealed class EnumMatchConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null && parameter is string name && string.Equals(value.ToString(), name, StringComparison.Ordinal);

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
    }

    /// <summary>Number (mm) ↔ text box, accepting a dot or a comma; bad text keeps the old value.</summary>
    public sealed class MmConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? d.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = (value as string ?? string.Empty).Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= 0 && d < 5000
                ? d
                : DependencyProperty.UnsetValue;
        }
    }
}
