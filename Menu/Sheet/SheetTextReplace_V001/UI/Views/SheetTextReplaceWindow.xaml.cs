using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using Revit26_Plugin_Revised.SheetTextReplace.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.UI.Views
{
    /// <summary>
    /// Sheet Text Replace window. Opens the sheet and views pop-ups, picks a sheet from the search
    /// list and shows a note in Revit on double-click; everything else is bound.
    /// </summary>
    public partial class SheetTextReplaceWindow : Window
    {
        private readonly SheetTextReplaceViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public SheetTextReplaceWindow(SheetTextReplaceViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.SheetTextReplace.Title;

            PreviewKeyDown += OnPreviewKeyDown;
            _viewModel.LogEntries.CollectionChanged += (_, _) => ScrollLogToEnd();
            Closed += (_, _) => _viewModel.Dispose();
        }

        /// <summary>Esc closes the open pop-up, or the window when none is open.</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (SheetPopup.IsOpen) SheetPopup.IsOpen = false;
            else if (ViewsPopup.IsOpen) ViewsPopup.IsOpen = false;
            else Close();
            e.Handled = true;
        }

        // ── Sheet picker ────────────────────────────────────────────────────

        private void SheetButton_Click(object sender, RoutedEventArgs e) => SheetPopup.IsOpen = true;

        private void SheetPopup_Opened(object sender, EventArgs e)
        {
            _viewModel.SheetSearchText = string.Empty;
            SheetList.SelectedItem = _viewModel.CurrentSheet;
            if (SheetList.SelectedItem != null) SheetList.ScrollIntoView(SheetList.SelectedItem);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SheetSearchBox.Focus()));
        }

        /// <summary>Enter picks the first match; Down moves into the list.</summary>
        private void SheetSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Pick(_viewModel.SheetsView.Cast<SheetItem>().FirstOrDefault());
                e.Handled = true;
            }
            else if (e.Key == Key.Down && SheetList.Items.Count > 0)
            {
                SheetList.SelectedIndex = 0;
                (SheetList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        }

        private void SheetList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is SheetItem sheet)
                Pick(sheet);
        }

        private void SheetList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SheetList.SelectedItem is SheetItem sheet)
            {
                Pick(sheet);
                e.Handled = true;
            }
        }

        private void Pick(SheetItem sheet)
        {
            if (sheet == null) return;
            SheetPopup.IsOpen = false;
            _viewModel.PickSheet(sheet);
        }

        // ── Views pop-up ────────────────────────────────────────────────────

        private void ViewsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewsPopup.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ViewFilterBox.Focus()));
        }

        // ── Preview grid ────────────────────────────────────────────────────

        /// <summary>Double-click on a row (not on its tick box) shows the note in Revit.</summary>
        private void PreviewGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (FindAncestor<ToggleButton>(source) != null) return;
            if (FindAncestor<DataGridRow>(source)?.DataContext is PreviewRowViewModel row)
                _viewModel.ShowNoteCommand.Execute(row);
        }

        // ── Log ─────────────────────────────────────────────────────────────

        private void ScrollLogToEnd()
        {
            if (LogListBox.Items.Count > 0)
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
            => LogClipboardService.CopySelected(LogListBox.SelectedItems);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private static T FindAncestor<T>(DependencyObject node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T match) return match;
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return null;
        }
    }
}
