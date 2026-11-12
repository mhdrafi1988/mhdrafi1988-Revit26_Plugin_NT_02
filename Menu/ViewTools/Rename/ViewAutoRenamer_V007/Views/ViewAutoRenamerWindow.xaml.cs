using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Views;

public partial class ViewsListWindow : Window
{
    private ViewsListViewModel Vm => (ViewsListViewModel)DataContext;

    // Tracks the last row index clicked — used for Shift+Click range selection
    private int _lastClickedIndex = -1;

    public ViewsListWindow(ViewsListViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.OwnerWindow = this; // owner of the cancel confirmation; Esc and window-close are handled while a run is active

        // Auto-scroll log panel to latest entry
        // Deferred: scrolling inside CollectionChanged makes WPF throw "ItemsControl is
        // inconsistent with its items source" when many lines arrive at once.
        vm.Logs.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[^1]);
            }));

        // 7.3.0: the log starts minimised; when it is expanded, show the latest line.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ViewsListViewModel.IsLogExpanded) || !vm.IsLogExpanded) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[^1]);
            }));
        };

        // 7.5.0: the views grid fills the height left in the window.
        Loaded += (_, _) =>
        {
            var body = Shell.BodyScrollViewer;
            if (body == null) return;
            body.ScrollChanged += (_, e) =>
            {
                // Inner lists (grid, log) bubble their own ScrollChanged; only the body's size matters.
                if (!ReferenceEquals(e.OriginalSource, body)) return;
                if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0) return;
                FitViewsGrid(body);
            };
            FitViewsGrid(body);
        };

        // Save filter/rename settings on close, per project convention.
        Closing += (_, _) => Vm.SaveSettings();
    }

    // ── Views grid height ────────────────────────────────────────────────────
    // The shell body is one ScrollViewer, so a list in it has no "fill" size of its own.
    // Give the grid whatever the viewport has left after the other body content (filter
    // bar, rename panel, toolbar, chips, log), never less than its MinHeight; below that
    // the body scrolls. Re-run whenever the viewport or the content height changes
    // (window resize, rename panel or log folded). Setting the height changes the extent
    // once more, but the second pass computes the same value and stops.
    private void FitViewsGrid(ScrollViewer body)
    {
        double others = body.ExtentHeight - ViewsGridControl.ActualHeight;
        double target = Math.Floor(body.ViewportHeight - others);
        if (double.IsNaN(target) || double.IsInfinity(target)) return;

        target = Math.Max(ViewsGridControl.MinHeight, target);
        if (double.IsNaN(ViewsGridControl.Height) || Math.Abs(ViewsGridControl.Height - target) > 0.5)
            ViewsGridControl.Height = target;
    }

    // ── Header checkbox ──────────────────────────────────────────────────────
    // Through the view model's bulk path: counts and previews update once, not once per row.
    private void HeaderCheckBox_Checked(object sender, RoutedEventArgs e) => Vm.SetSelection(_ => true);

    private void HeaderCheckBox_Unchecked(object sender, RoutedEventArgs e) => Vm.SetSelection(_ => false);

    // ── Row click — Ctrl+Click / Shift+Click extend checkbox selection.
    // A PLAIN click does NOT clear existing selections — selection state is
    // owned by the checkbox column per the project's DataGrid spec, and a
    // plain click's job here is only to let Shift-range-select anchor
    // correctly and to let text cells receive focus for editing. Wiping
    // other rows' checkboxes on a plain cell click would silently discard
    // an in-progress batch selection while the user is just trying to edit
    // one row's name — a bad interaction for a bulk-rename tool.
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        // Walk up the visual tree to find the DataGridRow that was clicked
        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row == null) return;

        // Ignore clicks on the checkbox cell itself — let the checkbox handle its own toggle
        var cell = FindAncestor<DataGridCell>((DependencyObject)e.OriginalSource);
        if (cell?.Column?.DisplayIndex == 0) return;

        if (row.Item is not ViewItemViewModel clickedItem) return;
        int clickedIndex = ViewsGridControl.Items.IndexOf(clickedItem);

        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            // Ctrl+Click — toggle individual row
            clickedItem.IsSelected = !clickedItem.IsSelected;
        }
        else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            // Shift+Click — range select from last clicked to current (additive)
            if (_lastClickedIndex < 0) _lastClickedIndex = clickedIndex;

            int from = Math.Min(_lastClickedIndex, clickedIndex);
            int to   = Math.Max(_lastClickedIndex, clickedIndex);

            for (int i = from; i <= to; i++)
            {
                if (ViewsGridControl.Items[i] is ViewItemViewModel item)
                    item.IsSelected = true;
            }
        }
        // Plain click: no selection change here — checkboxes own selection state.
        // _lastClickedIndex still updates below so a later Shift+Click anchors here.

        _lastClickedIndex = clickedIndex;
        e.Handled = false; // allow edit cells to still receive click / enter edit mode
    }

    // ── Keyboard navigation — Space toggles, ↑↓ moves focus ─────────────────
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Space && ViewsGridControl.CurrentItem is ViewItemViewModel focused)
        {
            focused.IsSelected = !focused.IsSelected;
            e.Handled = true;
        }
    }

    // ── Visual tree helper ───────────────────────────────────────────────────
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T found) return found;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void CopyAllLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

    private void CopySelectedLog_Click(object sender, RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
