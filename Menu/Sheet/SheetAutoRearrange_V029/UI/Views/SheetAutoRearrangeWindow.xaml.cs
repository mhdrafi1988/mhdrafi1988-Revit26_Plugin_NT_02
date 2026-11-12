using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.UI.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.UI.Views
{
    public partial class SheetAutoRearrangeWindow : Window
    {
        private readonly SheetAutoRearrangeViewModel _viewModel;

        public SheetAutoRearrangeWindow(SheetAutoRearrangeViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            Closed += (_, _) => _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

            // V011 NEW: save settings on window close, per suite convention
            // ("settings saved on both stage transitions and window close").
            // Also unhooks the DocumentChanged live-sync subscription so it
            // doesn't outlive the window.
            Closing += (_, _) =>
            {
                _viewModel.SaveSettings();
                _viewModel.Detach();
                _viewModel.Dispose();
            };

            Loaded += (_, _) => UpdatePreview();

            HookGapSettingsEvents();
            HookViewItemEvents();
        }

        /// <summary>
        /// GapSettings is its own ObservableObject (not replaced by reference
        /// when edited), so the top-level ViewModel's PropertyChanged never
        /// fires for margin/gap edits — hook its PropertyChanged directly so
        /// the live preview stays in sync. V009: no more per-group
        /// collection to iterate — GapSettings' own properties (margins +
        /// the two global gap values) are the only things that change.
        /// </summary>
        private void HookGapSettingsEvents()
        {
            _viewModel.GapSettings.PropertyChanged += (_, _) => UpdatePreview();
        }

        // V014: HookTallWideSettingsEvents REMOVED along with
        // TallDetectionSettings/WideDetectionSettings — RowFillStrategy is a
        // plain enum ObservableProperty on the ViewModel itself, so its
        // changes already flow through the existing ViewModel_PropertyChanged
        // switch below (no separate ObservableObject subscription needed).

        // ── View Types popover ──────────────────────────────────────────
        private void ViewTypesButton_Click(object sender, RoutedEventArgs e)
        {
            ViewTypesPopup.PlacementTarget = (UIElement)sender;
            ViewTypesPopup.Placement = PlacementMode.Bottom;
            ViewTypesPopup.IsOpen = !ViewTypesPopup.IsOpen;
        }

        // ── Grid checkbox: block row-select cascade, per DataGrid spec ──
        private void RowCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false; // allow the click to toggle the checkbox itself
            // Stops the click from also triggering DataGridRow selection:
            if (sender is DependencyObject d)
            {
                var row = FindParent<DataGridRow>(d);
                if (row != null)
                    row.IsSelected = false;
            }
        }

        // V009: AlgoRadio_Checked removed — Rearrange Method UI (and
        // algorithm selection generally) no longer exists; Sheet Order is
        // hardcoded in RearrangeEngine.

        private void OverflowRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag
                && System.Enum.TryParse<OverflowHandlingMode>(tag, out var mode))
            {
                _viewModel.OverflowHandlingMode = mode;
            }
        }

        // V014: TallOverflowRadio_Checked / WideOverflowRadio_Checked
        // REMOVED along with ShelfOverflowGrouping and the Tall/Wide anchor
        // system. RowFillStrategy is set directly by StrategyButton_Click
        // below.
        // V022 FIX: sender is now a plain Button, not ToggleButton — see
        // the StrategySegmentButton style comment in the XAML for why
        // ToggleButton was dropped (its native click-driven IsChecked
        // write silently broke the OneWay binding that used to drive the
        // "selected" visual). The "selected" state is now a MultiDataTrigger
        // in the style itself, driven purely by RowFillStrategy + Tag —
        // this handler's only job is still just updating the ViewModel.
        private void StrategyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag
                && System.Enum.TryParse<RowFillStrategy>(tag, out var strategy))
            {
                _viewModel.RowFillStrategy = strategy;
                UpdatePreview();
            }
        }

        // ── Live preview redraw ──────────────────────────────────────────
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SheetAutoRearrangeViewModel.Views):
                    HookViewItemEvents();
                    UpdatePreview();
                    break;

                case nameof(SheetAutoRearrangeViewModel.RowToleranceMm):
                case nameof(SheetAutoRearrangeViewModel.RowAlignment):
                case nameof(SheetAutoRearrangeViewModel.ColumnAlignH):
                case nameof(SheetAutoRearrangeViewModel.ColumnAlignV):
                case nameof(SheetAutoRearrangeViewModel.ColumnToleranceMm):
                case nameof(SheetAutoRearrangeViewModel.WithinColumnAlign):
                case nameof(SheetAutoRearrangeViewModel.LeftoverGroupMode):
                // V029: Align & Distribute options redraw the preview too.
                case nameof(SheetAutoRearrangeViewModel.AlignRows):
                case nameof(SheetAutoRearrangeViewModel.AlignColumns):
                case nameof(SheetAutoRearrangeViewModel.DistributeH):
                case nameof(SheetAutoRearrangeViewModel.DistributeV):
                    UpdatePreview();
                    break;

                // View Type filter popover: PreviewPack() now excludes any
                // type toggled off here (see ViewModel.PreviewPack), so the
                // Live Sheet Preview must redraw the moment a checkbox
                // changes — not just the grid's FilteredViewsSource.
                case nameof(SheetAutoRearrangeViewModel.ShowFloorPlan):
                case nameof(SheetAutoRearrangeViewModel.ShowSection):
                case nameof(SheetAutoRearrangeViewModel.ShowElevation):
                case nameof(SheetAutoRearrangeViewModel.Show3D):
                case nameof(SheetAutoRearrangeViewModel.ShowLegend):
                case nameof(SheetAutoRearrangeViewModel.ShowSchedule):
                case nameof(SheetAutoRearrangeViewModel.ShowDrafting):
                    UpdatePreview();
                    break;

                // V009: Placeable Area card removed — Region changes still
                // affect the Live Sheet Preview's scale math (via
                // Region.GetOverallBounds() in UpdatePreview), but there is
                // no longer a separate mini-preview to redraw alongside it.
                case nameof(SheetAutoRearrangeViewModel.Region):
                    UpdatePreview();
                    break;
            }
        }

        /// <summary>
        /// Each grid row's IsChecked toggle changes the ticked set PreviewPack()
        /// packs — hook every row's PropertyChanged so ticking/unticking
        /// refreshes the preview immediately. Unsubscribes from the previous
        /// Views collection's items first so replaced rows (after Load/Run)
        /// don't leak subscriptions on discarded ViewOnSheetItem instances.
        /// </summary>
        private System.Collections.Generic.List<ViewOnSheetItem> _hookedItems = new();

        private void HookViewItemEvents()
        {
            foreach (var old in _hookedItems)
                old.PropertyChanged -= ViewItem_PropertyChanged;

            _hookedItems = _viewModel.Views.ToList();
            foreach (var item in _hookedItems)
                item.PropertyChanged += ViewItem_PropertyChanged;
        }

        private void ViewItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewOnSheetItem.IsChecked))
            {
                UpdatePreview();
            }
        }

        /// <summary>
        /// Draws a to-scale preview of where Run() would actually place each
        /// ticked view, by calling the ViewModel's PreviewPack() — which runs
        /// the SAME packing service Run() uses, purely in memory. This keeps
        /// the preview in exact parity with what Run() will do; it is not an
        /// approximation based on current positions.
        ///
        /// V009 NOTE: the Placeable Area card (and its separate mini-preview,
        /// formerly DrawRegionPreview) has been removed from the UI entirely
        /// — this is now the only preview canvas in the tool. If Region is
        /// null (e.g. no title block detected), the canvas is cleared and
        /// left blank.
        /// </summary>
        private void UpdatePreview()
        {
            PreviewCanvas.Children.Clear();

            var region = _viewModel.Region;
            if (region == null)
                return;

            var placements = _viewModel.PreviewPack();
            if (placements.Count == 0)
                return;

            double canvasW = PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth : 340;
            double canvasH = PreviewCanvas.ActualHeight > 0 ? PreviewCanvas.ActualHeight : 200;

            var overallBounds = region.GetOverallBounds();
            double usableWFeet = overallBounds.Width;
            double usableHFeet = overallBounds.Height;
            if (usableWFeet <= 0) usableWFeet = 1;
            if (usableHFeet <= 0) usableHFeet = 1;

            // Overflow views are placed below the sheet's bottom edge
            // (continuing the row layout downward) rather than skipped —
            // extend the vertical scale span to include them so they're
            // visible in the preview instead of being clipped off-canvas.
            double lowestYFeet = overallBounds.Min.Y;
            foreach (var p in placements)
            {
                double bottomFeet = p.NewCenter.Y - (p.Item.HeightMm / 304.8) / 2.0;
                if (bottomFeet < lowestYFeet)
                    lowestYFeet = bottomFeet;
            }
            double totalHFeet = (overallBounds.Max.Y - lowestYFeet);
            if (totalHFeet <= 0) totalHFeet = usableHFeet;

            double scale = System.Math.Min(canvasW / usableWFeet, canvasH / totalHFeet) * 0.94;

            // Draw the sheet's bottom-edge boundary line so overflow content
            // below it reads as clearly outside the sheet, not just "lower".
            double sheetBottomYPx = canvasH;
            if (lowestYFeet < overallBounds.Min.Y)
            {
                var boundaryLine = new System.Windows.Shapes.Line
                {
                    X1 = 0,
                    X2 = canvasW,
                    Y1 = sheetBottomYPx,
                    Y2 = sheetBottomYPx,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F)),
                    StrokeThickness = 1,
                    StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 3 }
                };
                PreviewCanvas.Children.Add(boundaryLine);
            }

            foreach (var placement in placements)
            {
                double wFeet = placement.Item.WidthMm / 304.8;
                double hFeet = placement.Item.HeightMm / 304.8;

                double centerXFeet = placement.NewCenter.X - overallBounds.Min.X;
                double centerYFeet = placement.NewCenter.Y - overallBounds.Min.Y;

                double wPx = wFeet * scale;
                double hPx = hFeet * scale;
                double centerXPx = centerXFeet * scale;
                double centerYPx = canvasH - (centerYFeet * scale); // flip Y (sheet-up vs canvas-down)

                double leftPx = centerXPx - wPx / 2.0;
                double topPx = centerYPx - hPx / 2.0;

                var rect = new Rectangle
                {
                    Width = System.Math.Max(wPx, 4),
                    Height = System.Math.Max(hPx, 4),
                    Fill = placement.Fits ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xF8))
                                          : new SolidColorBrush(Color.FromRgb(0xFD, 0xED, 0xEC)),
                    Stroke = placement.Fits ? new SolidColorBrush(Color.FromRgb(0x2D, 0x6C, 0xDF))
                                             : new SolidColorBrush(Color.FromRgb(0xD9, 0x53, 0x4F)),
                    StrokeThickness = 1.2,
                    RadiusX = 2,
                    RadiusY = 2
                };

                Canvas.SetLeft(rect, leftPx);
                Canvas.SetTop(rect, topPx);
                PreviewCanvas.Children.Add(rect);
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            return parent as T;
        }

        private void CopyAllLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopyAll(LogList.Items);

        private void CopySelectedLog_Click(object sender, System.Windows.RoutedEventArgs e) => LogClipboardService.CopySelected(LogList.SelectedItems);

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
