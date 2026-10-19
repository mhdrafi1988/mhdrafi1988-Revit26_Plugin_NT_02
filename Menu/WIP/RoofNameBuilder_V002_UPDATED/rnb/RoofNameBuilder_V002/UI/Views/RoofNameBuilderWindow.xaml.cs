using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;
using Revit26_Plugin.RoofNameBuilder.V002.Core.Models;
using Revit26_Plugin.RoofNameBuilder.V002.UI.ViewModels;
using Revit26_Plugin.Shared.Services;
using Revit26_Plugin.Shared.Models;

namespace Revit26_Plugin.RoofNameBuilder.V002.UI.Views
{
    /// <summary>
    /// View-only code-behind: builds one "tick + value" column per applied code definition (the set is
    /// user-configurable, so it can't be static XAML), toggles the Types/Instances-only columns, feeds
    /// the log's selected items to the view model, and hosts the Export Log folder picker.
    /// </summary>
    public partial class RoofNameBuilderWindow : Window
    {
        private readonly RoofNameBuilderViewModel _viewModel;
        private readonly List<DataGridColumn> _codeColumns = new();

        public RoofNameBuilderWindow(RoofNameBuilderViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            BuildCodeColumns();
            ApplyModeColumns();

            viewModel.CodeColumnsChanged += (_, _) => BuildCodeColumns();
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            Closed += (_, _) =>
            {
                viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                viewModel.OnWindowClosed();
            };
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RoofNameBuilderViewModel.Mode)
                               or nameof(RoofNameBuilderViewModel.InstanceTargetParameter))
                ApplyModeColumns();
        }

        private void ApplyModeColumns()
        {
            var types = _viewModel.Mode == RoofNameMode.Types;
            CurrentNameColumn.Header = types ? "Current Name" : $"Current {_viewModel.InstanceTargetParameter}";
            InstanceCountColumn.Visibility = types ? Visibility.Visible : Visibility.Collapsed;
            TypeNameColumn.Visibility = types ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BuildCodeColumns()
        {
            foreach (var col in _codeColumns) RoofGrid.Columns.Remove(col);
            _codeColumns.Clear();

            var insertAt = RoofGrid.Columns.IndexOf(NewNameColumn);
            var inputStyle = (Style)FindResource("StyledTextInput");

            foreach (var def in _viewModel.AppliedCodeDefinitions.OrderBy(d => d.Order))
            {
                var col = new DataGridTemplateColumn
                {
                    Header = string.IsNullOrWhiteSpace(def.Unit) ? def.Code : $"{def.Code} ({def.Unit})",
                    Width = new DataGridLength(118),
                    CanUserSort = false,
                    CellTemplate = BuildCodeCellTemplate(def.Code, inputStyle)
                };
                if (!string.IsNullOrWhiteSpace(def.Label) || !string.IsNullOrWhiteSpace(def.SourceParameter))
                    col.HeaderStyle = BuildHeaderTooltipStyle($"{def.Label} — from '{def.SourceParameter}'");

                RoofGrid.Columns.Insert(insertAt++, col);
                _codeColumns.Add(col);
            }
        }

        private static DataTemplate BuildCodeCellTemplate(string code, Style inputStyle)
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            var tick = new FrameworkElementFactory(typeof(CheckBox));
            tick.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            tick.SetValue(MarginProperty, new Thickness(2, 0, 6, 0));
            tick.SetBinding(ToggleButton.IsCheckedProperty, new Binding($"Codes[{code}].Ticked")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.AppendChild(tick);

            var value = new FrameworkElementFactory(typeof(TextBox));
            value.SetValue(WidthProperty, 70.0);
            value.SetValue(StyleProperty, inputStyle);
            value.SetBinding(TextBox.TextProperty, new Binding($"Codes[{code}].Value")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.AppendChild(value);

            return new DataTemplate { VisualTree = panel };
        }

        private Style BuildHeaderTooltipStyle(string tooltip)
        {
            var style = new Style(typeof(DataGridColumnHeader), (Style)FindResource("StandardDataGridColumnHeader"));
            style.Setters.Add(new Setter(ToolTipProperty, tooltip));
            return style;
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _viewModel.SelectedLogEntries = LogList.SelectedItems.Cast<LogEntry>().ToList();

        private void ExportLogButton_Click(object sender, RoutedEventArgs e)
            => LogExportService.ExportWithPicker(LogList.Items, this, Title);

        private void CheckBoxCell_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is CheckBox cb)
            {
                cb.IsChecked = !cb.IsChecked;
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
