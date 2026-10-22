// =======================================================
// File: AutoSlopeByDrainWindow.xaml.cs
// Namespace: Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.UI.Views
// Changes vs V012:
//   - Owner set to Revit's main window here (was in the Command).
//   - One window at a time: the Command brings an open window to the front.
//   - Esc cancels a running job instead of closing; closing is blocked while
//     a run is in progress.
//   - Log copy/clear come from ToolViewModelBase; code-behind forwards the
//     ListBox selection and keeps the log scrolled to the end.
//   - Results side panel: forwards the grid selection for Zoom to / Copy rows.
//   - Kept from V012: the drain checkbox click handler and the drain-group
//     expander state (first group open, manual choices remembered per tab).
// =======================================================

using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.UI.Views
{
    public partial class AutoSlopeByDrainWindow : Window
    {
        private static AutoSlopeByDrainWindow _openWindow;
        private readonly AutoSlopeDrainViewModel _vm;

        public AutoSlopeByDrainWindow(AutoSlopeDrainViewModel viewModel, Autodesk.Revit.UI.UIApplication app)
        {
            InitializeComponent();
            _vm = viewModel;
            DataContext = viewModel;

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
            else
                Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void LogListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _vm.SelectedLogEntries = LogListBox.SelectedItems.Cast<LogEntry>().ToList();

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

        // ── Results side panel ──────────────────────────────────────────────
        private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.IsVisible)
                _vm.SetSelectedResultRows(grid.SelectedItems.Cast<DrainResultRow>().ToList());
        }

        // ── Marker line style picked: keep its name for settings ────────────
        private void LineStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            _vm.SyncLineStyleNames();

        // ── Drain grid checkbox: toggle without the DataGrid row-select cascade ─
        // PreviewMouseLeftButtonDown tunnels before the CheckBox's own click
        // handling; marking it handled stops that, so IsChecked is toggled here.
        private void DrainCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is CheckBox cb && cb.IsEnabled)
            {
                cb.IsChecked = !(cb.IsChecked ?? false);
                e.Handled = true;
            }
        }

        // ── Drain group expanders (from V010) ───────────────────────────────

        /// <summary>
        /// Sets IsExpanded from the roof tab's manual choices, or opens only the
        /// first group in the current view (Circle when there is one).
        /// </summary>
        private void DrainGroupExpander_Loaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Expander expander)) return;
            if (!(expander.DataContext is CollectionViewGroup group)) return;

            var tabVm = FindOwningRoofTabViewModel(expander);
            if (tabVm == null) return;

            string groupName = group.Name?.ToString() ?? "";
            bool isFirstGroupInView = tabVm.FilteredDrainsView.Groups?.Count > 0
                && (tabVm.FilteredDrainsView.Groups[0] as CollectionViewGroup)?.Name?.ToString() == groupName;

            expander.IsExpanded = tabVm.GroupExpandedOverride.TryGetValue(groupName, out bool overridden)
                ? overridden
                : isFirstGroupInView;
        }

        /// <summary>Remembers a manual expand/collapse until the size filter changes.</summary>
        private void DrainGroupExpander_ExpandedOrCollapsed(object sender, RoutedEventArgs e)
        {
            if (!(sender is Expander expander)) return;
            if (!(expander.DataContext is CollectionViewGroup group)) return;

            var tabVm = FindOwningRoofTabViewModel(expander);
            if (tabVm == null) return;

            tabVm.GroupExpandedOverride[group.Name?.ToString() ?? ""] = expander.IsExpanded;
        }

        private static RoofTabViewModel FindOwningRoofTabViewModel(DependencyObject start)
        {
            for (DependencyObject current = start; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is DataGrid dataGrid && dataGrid.DataContext is RoofTabViewModel tabVm)
                    return tabVm;
            }
            return null;
        }
    }
}
