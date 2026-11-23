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
    /// Sheet Text Replace window. Opens the scope and views pop-ups, picks a sheet or a single view
    /// from the search list and shows a note in Revit on double-click; everything else is bound.
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
            if (ScopePopup.IsOpen) ScopePopup.IsOpen = false;
            else if (ViewsPopup.IsOpen) ViewsPopup.IsOpen = false;
            else if (RecentPopup.IsOpen) RecentPopup.IsOpen = false;
            else Close();
            e.Handled = true;
        }

        // ── Scope picker (sheet or single view) ─────────────────────────────

        private void ScopeButton_Click(object sender, RoutedEventArgs e) => ScopePopup.IsOpen = true;

        private void ScopePopup_Opened(object sender, EventArgs e)
        {
            _viewModel.ScopeSearchText = string.Empty;
            ScopeList.SelectedItem = _viewModel.CurrentScope;
            if (ScopeList.SelectedItem != null) ScopeList.ScrollIntoView(ScopeList.SelectedItem);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ScopeSearchBox.Focus()));
        }

        /// <summary>Enter picks the first match; Down moves into the list.</summary>
        private void ScopeSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Pick(_viewModel.ScopesView.Cast<ScopeItem>().FirstOrDefault());
                e.Handled = true;
            }
            else if (e.Key == Key.Down && ScopeList.Items.Count > 0)
            {
                ScopeList.SelectedIndex = 0;
                (ScopeList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        }

        private void ScopeList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is ScopeItem scope)
                Pick(scope);
        }

        private void ScopeList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ScopeList.SelectedItem is ScopeItem scope)
            {
                Pick(scope);
                e.Handled = true;
            }
        }

        private void Pick(ScopeItem scope)
        {
            if (scope == null) return;
            ScopePopup.IsOpen = false;
            _viewModel.PickScope(scope);
        }

        // ── Views pop-up ────────────────────────────────────────────────────

        private void ViewsButton_Click(object sender, RoutedEventArgs e)
        {
            ViewsPopup.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ViewFilterBox.Focus()));
        }

        // ── Recent searches pop-up ──────────────────────────────────────────

        private void RecentButton_Click(object sender, RoutedEventArgs e)
        {
            RecentPopup.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => RecentList.Focus()));
        }

        private void RecentList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is SavedSearch search)
                UseRecent(search);
        }

        private void RecentList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && RecentList.SelectedItem is SavedSearch search)
            {
                UseRecent(search);
                e.Handled = true;
            }
        }

        private void RecentClear_Click(object sender, RoutedEventArgs e) => RecentPopup.IsOpen = false;

        private void UseRecent(SavedSearch search)
        {
            RecentPopup.IsOpen = false;
            _viewModel.UseRecent(search);
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
