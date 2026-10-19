// =======================================================
// File: RoofPointsAndSlopeWindow.xaml.cs
// Location: UI/Views/
// Modeless, owned by Revit, one window at a time. Settings are saved on
// close; closing is blocked while a run is in progress.
// =======================================================

using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.UI.Views
{
    /// <summary>The Roof Points &amp; Slope window.</summary>
    public partial class RoofPointsAndSlopeWindow : Window
    {
        private static RoofPointsAndSlopeWindow _openWindow;
        private readonly RoofPointsAndSlopeViewModel _vm;

        /// <summary>Creates the window, owned by Revit's main window.</summary>
        public RoofPointsAndSlopeWindow(RoofPointsAndSlopeViewModel vm, IntPtr ownerHandle)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            new WindowInteropHelper(this).Owner = ownerHandle;
            _openWindow = this;
        }

        /// <summary>Brings an open window to the front. Returns false when none is open.</summary>
        public static bool TryActivateOpenWindow()
        {
            if (_openWindow == null) return false;
            if (_openWindow.WindowState == WindowState.Minimized)
                _openWindow.WindowState = WindowState.Normal;
            _openWindow.Activate();
            return true;
        }

        /// <inheritdoc/>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_vm.IsRunning)
            {
                _vm.Log(LogLevel.Warning, "Wait for the run to finish before closing.");
                e.Cancel = true;
                return;
            }

            _vm.SaveSettings();
            base.OnClosing(e);
        }

        /// <inheritdoc/>
        protected override void OnClosed(EventArgs e)
        {
            if (ReferenceEquals(_openWindow, this)) _openWindow = null;
            base.OnClosed(e);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAllLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Folder for the Excel report",
                InitialDirectory = _vm.ExportFolder
            };
            if (dialog.ShowDialog(this) == true)
                _vm.ExportFolder = dialog.FolderName;
        }
    }
}
