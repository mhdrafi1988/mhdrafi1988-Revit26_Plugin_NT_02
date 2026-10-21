using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.UI.Views
{
    /// <summary>Modeless window of the Roof Shape From Link tool. One window at a time.</summary>
    public partial class RoofShapeFromLinkWindow : Window
    {
        private static RoofShapeFromLinkWindow _openWindow;
        private readonly RoofShapeFromLinkViewModel _vm;

        /// <summary>Creates the window for <paramref name="vm"/>.</summary>
        public RoofShapeFromLinkWindow(RoofShapeFromLinkViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            _vm.PickStarting += () => Hide();
            _vm.PickEnded += () => { Show(); Activate(); };
            _vm.LogEntries.CollectionChanged += OnLogChanged;
            _openWindow = this;
        }

        /// <summary>Brings the open window to the front. False when none is open.</summary>
        public static bool TryActivateOpenWindow()
        {
            if (_openWindow == null) return false;
            if (_openWindow.WindowState == WindowState.Minimized) _openWindow.WindowState = WindowState.Normal;
            _openWindow.Activate();
            return true;
        }

        /// <inheritdoc/>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_vm.IsRunning)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        /// <inheritdoc/>
        protected override void OnClosed(System.EventArgs e)
        {
            _vm.LogEntries.CollectionChanged -= OnLogChanged;
            _openWindow = null;
            base.OnClosed(e);
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAllLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);
    }
}
