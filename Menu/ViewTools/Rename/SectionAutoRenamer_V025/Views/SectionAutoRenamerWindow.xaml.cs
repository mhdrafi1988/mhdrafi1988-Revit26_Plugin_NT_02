using Revit26_Plugin_Revised.SectionAutoRenamer.V025.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SectionAutoRenamer.V025.Views;

public partial class SectionsListWindow : Window
{
    private SectionsListViewModel Vm => (SectionsListViewModel)DataContext;

    // Tracks the last row index clicked — used for Shift+Click range selection
    private int _lastClickedIndex = -1;

    public SectionsListWindow(SectionsListViewModel vm)
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
    }

    // ── "Set selected" — commit on Enter without needing focus loss ─────────
    private void SetSelectedTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var tb = (TextBox)sender;
        var expr = tb.GetBindingExpression(TextBox.TextProperty);
        expr?.UpdateSource();
        e.Handled = true;
    }

    // ── Header checkbox ──────────────────────────────────────────────────────
    private void HeaderCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        foreach (var item in SectionsGrid.Items.OfType<SectionItemViewModel>())
            item.IsSelected = true;
    }

    private void HeaderCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        foreach (var item in SectionsGrid.Items.OfType<SectionItemViewModel>())
            item.IsSelected = false;
    }

    // ── Row click — Ctrl+Click multi, Shift+Click range, plain click single ──
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        // Walk up the visual tree to find the DataGridRow that was clicked
        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row == null) return;

        // Ignore clicks on the checkbox cell itself — let the checkbox handle its own toggle
        var cell = FindAncestor<DataGridCell>((DependencyObject)e.OriginalSource);
        if (cell?.Column?.DisplayIndex == 0) return;

        if (row.Item is not SectionItemViewModel clickedItem) return;
        int clickedIndex = SectionsGrid.Items.IndexOf(clickedItem);

        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            // Ctrl+Click — toggle individual row
            clickedItem.IsSelected = !clickedItem.IsSelected;
        }
        else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            // Shift+Click — range select from last clicked to current
            if (_lastClickedIndex < 0) _lastClickedIndex = 0;

            int from = Math.Min(_lastClickedIndex, clickedIndex);
            int to   = Math.Max(_lastClickedIndex, clickedIndex);

            for (int i = from; i <= to; i++)
            {
                if (SectionsGrid.Items[i] is SectionItemViewModel item)
                    item.IsSelected = true;
            }
        }
        else
        {
            // Plain click — select only this row
            foreach (var item in SectionsGrid.Items.OfType<SectionItemViewModel>())
                item.IsSelected = false;
            clickedItem.IsSelected = true;
        }

        _lastClickedIndex = clickedIndex;
        e.Handled = false; // allow edit cells to still receive click
    }

    // ── Keyboard navigation — Space toggles, ↑↓ moves focus ─────────────────
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Space && SectionsGrid.CurrentItem is SectionItemViewModel focused)
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
