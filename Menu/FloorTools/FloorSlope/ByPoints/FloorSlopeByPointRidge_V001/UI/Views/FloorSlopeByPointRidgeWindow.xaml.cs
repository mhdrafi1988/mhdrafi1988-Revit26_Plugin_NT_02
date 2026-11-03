// =======================================================
// File: FloorSlopeByPointRidgeWindow.xaml.cs
// Fixes:
//   #1  Corrected namespace to match XAML (041)
//   #2  Removed unused Action<string> log parameter from
//       AutoSlopeViewModel constructor call.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.UI.ViewModels;   // ensure ViewModel namespace matches
using Revit26_Plugin_Revised.Shared.Models;                          // LogEntry
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.UI.Views      // âœ… changed from _04 to _041
{
    public partial class FloorSlopeByPointRidgeWindow : Window
    {
        public FloorSlopeByPointRidgeWindow(
            UIDocument uidoc,
            UIApplication app,
            ElementId floorId,
            List<XYZ> drains)
        {
            InitializeComponent();

            var viewModel = new AutoSlopeViewModel(uidoc, app, floorId, drains);
            DataContext = viewModel;
            this.Focus();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Persist last-used field values/options (V026) â€” window size/position
            // is intentionally NOT part of this scope.
            if (DataContext is AutoSlopeViewModel vm)
                vm.SaveAllSettings();

            base.OnClosing(e);
        }

        // â”€â”€ Esc = Close (modeless dialog convention) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // â”€â”€ Copy All â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void CopyAllLogs_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AutoSlopeViewModel vm || vm.LogEntries.Count == 0) return;

            string text = string.Join(Environment.NewLine, vm.LogEntries.Select(entry => entry.ToString()));
            TrySetClipboardText(text);
        }

        // â”€â”€ Copy Selected (rows highlighted in the log ListBox) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            var selected = LogListBox.SelectedItems.Cast<LogEntry>().ToList();
            if (selected.Count == 0) return;

            string text = string.Join(Environment.NewLine, selected.Select(entry => entry.ToString()));
            TrySetClipboardText(text);
        }

        private static void TrySetClipboardText(string text)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
            }
            catch
            {
                // Clipboard can be transiently locked by another process â€” fail silently,
                // consistent with this suite's "never block the UI on a non-critical error" rule.
            }
        }
    }
}
