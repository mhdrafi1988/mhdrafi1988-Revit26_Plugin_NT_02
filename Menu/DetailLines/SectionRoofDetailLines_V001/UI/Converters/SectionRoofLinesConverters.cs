using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.Converters
{
    /// <summary>
    /// A section row's status to the colour it reads in: done, worth noting, or blocked.
    /// Matches the status colours in Shared/SharedStyles.xaml.
    /// </summary>
    public class SectionStatusBrushConverter : IValueConverter
    {
        private static readonly Brush Done = Frozen(0x2E, 0x7D, 0x4F);
        private static readonly Brush Note = Frozen(0x9A, 0x5B, 0x00);
        private static readonly Brush Blocked = Frozen(0xB2, 0x3A, 0x35);
        private static readonly Brush Plain = Frozen(0x5B, 0x71, 0x85);

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is SectionRowStatus status
                ? status switch
                {
                    SectionRowStatus.Done => Done,
                    SectionRowStatus.Note => Note,
                    SectionRowStatus.Blocked => Blocked,
                    _ => Plain
                }
                : Plain;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>An element role to the chip text beside a category: what the tool does with it.</summary>
    public class ElementRoleChipConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ElementRole role
                ? role switch
                {
                    ElementRole.Roof => "cut profile",
                    ElementRole.Wall => "wall line",
                    _ => "listed"
                }
                : string.Empty;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// An element role to the chip's background, or to its foreground with "Foreground" as the
    /// converter parameter.
    /// </summary>
    public class ElementRoleBrushConverter : IValueConverter
    {
        private static readonly Brush RoofBack = Frozen(0xE6, 0xF4, 0xEC);
        private static readonly Brush RoofText = Frozen(0x2E, 0x7D, 0x4F);
        private static readonly Brush WallBack = Frozen(0xE6, 0xEE, 0xFB);
        private static readonly Brush WallText = Frozen(0x1F, 0x5F, 0xBF);
        private static readonly Brush ListedBack = Frozen(0xEC, 0xEF, 0xF3);
        private static readonly Brush ListedText = Frozen(0x5B, 0x71, 0x85);

        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool foreground = string.Equals(parameter as string, "Foreground", StringComparison.Ordinal);
            if (value is not ElementRole role) return foreground ? ListedText : ListedBack;
            return role switch
            {
                ElementRole.Roof => foreground ? RoofText : RoofBack,
                ElementRole.Wall => foreground ? WallText : WallBack,
                _ => foreground ? ListedText : ListedBack
            };
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// A line-style name to what the picker shows: an empty name reads as the view's own default
    /// style rather than as a blank row.
    /// </summary>
    public class LineStyleNameConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? "(the view's default line style)" : value;

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
