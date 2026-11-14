using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V007
{
    public partial class RoofEdgeSectionsWindow : Window
    {
        private readonly RoofEdgeSectionsViewModel _viewModel;
        private readonly UIApplication _uiApp;

        public RoofEdgeSectionsWindow(RoofEdgeSectionsViewModel viewModel, UIApplication uiApp, IntPtr revitMainWindowHandle)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _uiApp = uiApp;
            DataContext = _viewModel;

            // Per convention: WindowInteropHelper with the Revit main window handle,
            // never System.Windows.Application.Current.MainWindow.
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = revitMainWindowHandle;

            _viewModel.CopyLogsRequested = OnCopyLogsRequested;
            _viewModel.ExportLogsRequested = OnExportLogsRequested;
            _viewModel.RequestOpenViewsIfNeeded = OnRequestOpenViewsIfNeeded;

            Closing += RoofEdgeSectionsWindow_Closing;
            PreviewKeyDown += RoofEdgeSectionsWindow_PreviewKeyDown;
        }

        // Esc closes the "Open created sections" popover (and only that).
        private void RoofEdgeSectionsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _viewModel.IsOpenSectionsPopoverOpen)
            {
                _viewModel.IsOpenSectionsPopoverOpen = false;
                e.Handled = true;
            }
        }

        // Clicking the dimmed area around the popover closes it.
        private void PopoverBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _viewModel.IsOpenSectionsPopoverOpen = false;
            e.Handled = true;
        }

        private void RoofEdgeSectionsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel.OnWindowClosing();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Blocks the checkbox click from also triggering DataGrid row selection,
        // per DataGrid checkbox-column convention.
        private void CheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false; // allow the checkbox itself to toggle; only suppress row-select cascade
            if (sender is FrameworkElement fe)
            {
                var row = FindParent<System.Windows.Controls.DataGridRow>(fe);
                if (row != null)
                    row.IsSelected = false;
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        private void OnCopyLogsRequested(List<LogEntry> entries)
        {
            if (entries.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.AppendLine(e.ToString());
            System.Windows.Clipboard.SetText(sb.ToString());
        }

        private void OnExportLogsRequested(List<LogEntry> entries, string lastFolder)
        {
            if (entries.Count == 0) return;

            string folder = lastFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select folder to save RoofEdgeSections logs"
                };
                if (dialog.ShowDialog() != true)
                    return;
                folder = dialog.FolderName;
            }

            LogExportHelper.Export(entries, folder);
        }

        // "Open All" mode only. "Ask Me" shows the in-window popover instead (ViewModel).
        private void OnRequestOpenViewsIfNeeded(List<ElementId> createdViewIds, string openViewsMode)
        {
            if (createdViewIds.Count == 0 || openViewsMode != "OpenAll") return;

            Document doc = _uiApp.ActiveUIDocument.Document;
            foreach (ElementId id in createdViewIds)
            {
                if (doc.GetElement(id) is View view)
                {
                    try { _uiApp.ActiveUIDocument.ActiveView = view; }
                    catch { /* best-effort â€” some views may not be openable in the current context */ }
                }
            }
        }
    }
}

