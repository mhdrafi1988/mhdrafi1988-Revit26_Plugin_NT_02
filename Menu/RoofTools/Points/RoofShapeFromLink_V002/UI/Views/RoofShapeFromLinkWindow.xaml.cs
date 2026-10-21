using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Revit26_Plugin_Revised.RoofShapeFromLink.V002.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V002.UI.Views
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
            _vm.ResetViewRequested += OnResetView;
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

        /// <summary>Clears the sort arrows and shows every column again.</summary>
        private void OnResetView()
        {
            foreach (DataGridColumn column in PointsGrid.Columns)
            {
                column.SortDirection = null;
                column.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Opens the column chooser: one tick per column, except the tick column.</summary>
        private void Columns_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = ColumnsButton, Placement = PlacementMode.Bottom };
            foreach (DataGridColumn column in PointsGrid.Columns.Skip(1))
            {
                DataGridColumn target = column;
                var item = new MenuItem
                {
                    Header = target.Header?.ToString(),
                    IsCheckable = true,
                    IsChecked = target.Visibility == Visibility.Visible,
                    StaysOpenOnClick = true
                };
                item.Click += (_, _) => target.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAllLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);
    }
}
