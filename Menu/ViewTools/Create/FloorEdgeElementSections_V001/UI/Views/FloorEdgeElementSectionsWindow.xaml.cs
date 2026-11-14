using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    public partial class FloorEdgeElementSectionsWindow : Window
    {
        private readonly FloorEdgeElementSectionsViewModel _viewModel;
        private readonly UIApplication _uiApp;

        public FloorEdgeElementSectionsWindow(FloorEdgeElementSectionsViewModel viewModel, UIApplication uiApp, IntPtr revitMainWindowHandle)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _uiApp = uiApp;
            DataContext = _viewModel;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            helper.Owner = revitMainWindowHandle;

            _viewModel.CopyLogsRequested = OnCopyLogsRequested;
            _viewModel.ExportLogsRequested = OnExportLogsRequested;
            _viewModel.RequestOpenViewsIfNeeded = OnRequestOpenViewsIfNeeded;
            _viewModel.HideWindow = Hide;
            _viewModel.ShowWindow = Show;

            Closing += FloorEdgeElementSectionsWindow_Closing;
        }

        private void FloorEdgeElementSectionsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel.OnWindowClosing();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;
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
                    Title = "Select folder to save FloorEdgeElementSections logs"
                };
                if (dialog.ShowDialog() != true)
                    return;
                folder = dialog.FolderName;
            }

            LogExportHelper.Export(entries, folder);
        }

        private void OnRequestOpenViewsIfNeeded(List<ElementId> createdViewIds, string openViewsMode)
        {
            if (createdViewIds.Count == 0) return;

            bool shouldOpen;
            if (openViewsMode == "OpenAll") shouldOpen = true;
            else if (openViewsMode == "DontOpen") shouldOpen = false;
            else
            {
                var tdOpen = new TaskDialog("Floor Edge Element Sections");
                tdOpen.MainContent = $"{createdViewIds.Count} section view(s) were created. Open them now?";
                tdOpen.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                shouldOpen = tdOpen.Show() == TaskDialogResult.Yes;
            }

            if (!shouldOpen) return;

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

