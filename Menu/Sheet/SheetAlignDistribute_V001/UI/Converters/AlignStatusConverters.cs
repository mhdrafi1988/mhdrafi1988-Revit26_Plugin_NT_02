using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.Converters
{
    /// <summary>
    /// Colours the status pill in the Elements grid from the row's <see cref="AlignStatus"/>.
    /// The brushes come from <c>Shared/SharedStyles.xaml</c>, so the pill matches every other
    /// tool's status colours and no colour is written into the window.
    /// </summary>
    public sealed class AlignStatusToBrushConverter : IValueConverter
    {
        /// <summary>Which of the three brushes of a status token to return.</summary>
        public enum Part
        {
            /// <summary>The pill's fill.</summary>
            Background,

            /// <summary>The pill's text.</summary>
            Foreground,

            /// <summary>The pill's outline.</summary>
            Border
        }

        /// <summary>Which brush this instance returns; set in XAML.</summary>
        public Part Target { get; set; } = Part.Background;

        /// <summary>Maps a status to one of the shared status brushes.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string token = value is AlignStatus status ? status switch
            {
                AlignStatus.Move => "Info",
                AlignStatus.InPlace => "Ok",
                AlignStatus.OverLimit => "Warn",
                AlignStatus.Blocked => "Error",
                AlignStatus.NoPartner => "Warn",
                _ => null
            } : null;

            if (token == null)
                return Lookup(Target switch
                {
                    Part.Foreground => "BrushTextSecondary",
                    Part.Border => "BrushBorderDefault",
                    _ => "BrushSystemSecondaryBackground"
                });

            return Lookup(Target switch
            {
                Part.Foreground => $"BrushStatus{token}",
                Part.Border => $"BrushStatus{token}Border",
                _ => $"BrushStatus{token}Soft"
            });
        }

        /// <summary>Not supported; the pill is display only.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush Lookup(string key)
            => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    /// <summary>True when the value is null — used to grey out the clearance boxes with nothing selected.</summary>
    public sealed class IsNotNullConverter : IValueConverter
    {
        /// <summary>Returns true when <paramref name="value"/> is not null.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value != null;

        /// <summary>Not supported.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
