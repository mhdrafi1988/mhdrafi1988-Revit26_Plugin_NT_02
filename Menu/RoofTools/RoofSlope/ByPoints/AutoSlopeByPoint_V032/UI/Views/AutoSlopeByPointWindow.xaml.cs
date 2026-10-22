// =======================================================
// File: AutoSlopeByPointWindow.xaml.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.UI.Views
// Changes vs V030:
//   - Owner set to Revit's main window via WindowInteropHelper (CLAUDE.md).
//   - One window at a time: the command brings an open window to the front.
//   - Esc cancels a running job instead of closing; closing is blocked
//     while a run is in progress (B7).
//   - Log copy/clear moved to ToolViewModelBase commands; code-behind only
//     forwards the ListBox selection and keeps the log scrolled to the end.
// =======================================================

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.UI.Views
{
    public partial class AutoSlopeByPointWindow : Window
    {
        private static AutoSlopeByPointWindow _openWindow;
        private readonly AutoSlopeViewModel _vm;

        public AutoSlopeByPointWindow(UIApplication app, RoofBase roof, List<Point3> drains)
        {
            InitializeComponent();

            _vm = new AutoSlopeViewModel(app, roof, drains);
            DataContext = _vm;

            try { new WindowInteropHelper(this).Owner = app.MainWindowHandle; }
            catch { /* owner is a convenience; never block the window */ }

            _vm.LogEntries.CollectionChanged += OnLogChanged;
            _openWindow = this;
        }

        /// <summary>Brings the open window to the front. False when none is open.</summary>
        public static bool TryActivateOpenWindow()
        {
            if (_openWindow == null) return false;
            if (_openWindow.WindowState == WindowState.Minimized)
                _openWindow.WindowState = WindowState.Normal;
            _openWindow.Activate();
            return true;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_vm.CanClose())
            {
                e.Cancel = true;
                return;
            }
            _vm.SaveSettings();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.LogEntries.CollectionChanged -= OnLogChanged;
            _vm.Dispose();
            if (ReferenceEquals(_openWindow, this)) _openWindow = null;
            base.OnClosed(e);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;

            if (_vm.CancelRunCommand.CanExecute(null))
                _vm.CancelRunCommand.Execute(null);
            else if (_vm.State != RunState.Picking)
                Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void LogListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
            _vm.SelectedLogEntries = LogListBox.SelectedItems.Cast<LogEntry>().ToList();

        // V032: results side panel — forward the grid selection for Zoom to / Copy rows.
        private void ResultsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid grid && grid.IsVisible)
                _vm.SetSelectedResultRows(grid.SelectedItems.Cast<ResultRow>().ToList());
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || !LogListBox.IsVisible) return;
            // Deferred: scrolling inside CollectionChanged makes WPF throw
            // "ItemsControl is inconsistent with its items source".
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                int count = LogListBox.Items.Count;
                if (count > 0) LogListBox.ScrollIntoView(LogListBox.Items[count - 1]);
            }));
        }
    }
}
