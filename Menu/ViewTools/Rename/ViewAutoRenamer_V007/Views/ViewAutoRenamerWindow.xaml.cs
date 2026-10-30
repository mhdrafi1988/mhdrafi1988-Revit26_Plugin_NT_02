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

        // Auto-scroll log panel to latest entry
        // Deferred: scrolling inside CollectionChanged makes WPF throw "ItemsControl is
        // inconsistent with its items source" when many lines arrive at once.
        vm.Logs.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new System.Action(() =>
            {
                if (LogList.Items.Count > 0)
                    LogList.ScrollIntoView(LogList.Items[^1]);
            }));

        // Save filter/rename settings on close, per project convention.
        Closing += (_, _) => Vm.SaveSettings();

        // Esc closes; Ctrl+A selects every visible row.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !(e.OriginalSource is TextBox)) { Close(); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && !(e.OriginalSource is TextBox))
            {
                Vm.SelectAllCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    // Keeps the popover's Load button in step with the tick boxes.
    private void LoadTypeTick_Changed(object sender, RoutedEventArgs e) => Vm.OnLoadTypeTickChanged();

    // ── Header checkbox ──────────────────────────────────────────────────────
    private void HeaderCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewsGridControl.Items.OfType<ViewItemViewModel>())
            item.IsSelected = true;
    }

    private void HeaderCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewsGridControl.Items.OfType<ViewItemViewModel>())
            item.IsSelected = false;
    }

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
