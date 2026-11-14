using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.Views
{
    /// <summary>
    /// Maps a <see cref="RowState"/> or <see cref="CheckResult"/> to the chip brushes of SharedStyles.xaml.
    /// ConverterParameter: "Background", "Border" or "Foreground" (default).
    /// </summary>
    public sealed class StatusBrushConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string tone = value switch
            {
                CheckResult.Pass => "Ok",
                CheckResult.Warning => "Warn",
                CheckResult.Blocked => "Error",
                RowState.Ready or RowState.Assigned => "Ok",
                RowState.Warning or RowState.Skipped => "Warn",
                RowState.Blocked or RowState.Failed => "Error",
                RowState.Pending => "Info",
                _ => "Muted"
            };
            string part = parameter as string ?? "Foreground";
            string key = (tone, part) switch
            {
                ("Muted", "Background") => "BrushSystemSecondaryBackground",
                ("Muted", "Border") => "BrushBorderDefault",
                ("Muted", _) => "BrushTextSecondary",
                (_, "Background") => $"BrushStatus{tone}Soft",
                (_, "Border") => $"BrushStatus{tone}Border",
                _ => $"BrushStatus{tone}"
            };
            return Application.Current?.TryFindResource(key) as Brush
                   ?? (Brush)(Lookup(key) ?? Brushes.Gray);
        }

        /// <inheritdoc/>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;

        // Revit has no WPF Application with our resources, so read SharedStyles.xaml once.
        private static ResourceDictionary _styles;

        private static object Lookup(string key)
        {
            try
            {
                _styles ??= new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Revit26_Plugin_Revised;component/Shared/SharedStyles.xaml")
                };
                return _styles.Contains(key) ? _styles[key] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
