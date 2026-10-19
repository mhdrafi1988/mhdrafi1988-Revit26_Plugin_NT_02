// IslandRoofSplitter V002 — window code-behind: wiring only (DataContext, close/hide events, multi-select lists).
#nullable enable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.UI.Views
{
    public partial class IslandRoofSplitterWindow : Window
    {
        private readonly IslandRoofSplitterViewModel _vm;

        public IslandRoofSplitterWindow(IslandRoofSplitterViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            vm.RequestClose += OnRequestClose;
            vm.RequestHide += OnRequestHide;
            Closed += OnClosed;
        }

        private void OnRequestClose() => Close();

        private void OnRequestHide(bool hide)
        {
            if (hide) Hide();
            else { Show(); Activate(); }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _vm.RequestClose -= OnRequestClose;
            _vm.RequestHide -= OnRequestHide;
            _vm.OnWindowClosed();
        }

        private void IslandsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _vm.SelectedIslands = IslandsGrid.SelectedItems.OfType<IslandRowViewModel>().ToList();

        private void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for the log file" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string path = LogExportService.Export("IslandRoofSplitter", _vm.LogEntries, dlg.FolderName);
                MessageBox.Show(this, $"Log exported to:\n{path}", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not export log:\n{ex.Message}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>ToolViewModelBase.CopySelectedLogCommand reads SelectedLogEntries.</summary>
        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _vm.SelectedLogEntries = LogList.SelectedItems.OfType<LogEntry>().ToList();
    }
}
