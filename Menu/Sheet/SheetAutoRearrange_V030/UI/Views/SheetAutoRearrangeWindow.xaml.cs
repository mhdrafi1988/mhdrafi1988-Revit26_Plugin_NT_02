using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.UI.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.UI.Views
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

            Loaded += (_, _) =>
            {
                FitMainGrid();
                UpdatePreview();
            };
            SizeChanged += (_, _) => FitMainGrid();

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
                case nameof(SheetAutoRearrangeViewModel.RowFillStrategy):
                case nameof(SheetAutoRearrangeViewModel.EnablePriorityGroups):
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
        // Window chrome + shell header, footer, status strip and the collapsed log bar around the main grid.
        private const double ChromeHeight = 300;

        /// <summary>Keeps the three-column area as tall as the window allows, so the preview uses the space.</summary>
        private void FitMainGrid() => MainGrid.Height = System.Math.Max(400, ActualHeight - ChromeHeight);

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreview();

        private static readonly Brush SheetFill = Brushes.White;
        private static readonly Brush SheetStroke = new SolidColorBrush(Color.FromRgb(0xB5, 0xC2, 0xCF));
        private static readonly Brush UsableStroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x4F));
        private static readonly Brush FitFill = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xF8));
        private static readonly Brush FitStroke = new SolidColorBrush(Color.FromRgb(0x2D, 0x6C, 0xDF));
        private static readonly Brush OverflowFill = new SolidColorBrush(Color.FromRgb(0xFB, 0xE8, 0xE7));
        private static readonly Brush OverflowStroke = new SolidColorBrush(Color.FromRgb(0xB2, 0x3A, 0x35));
        private static readonly Brush NameText = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F));

        /// <summary>
        /// Redraws the preview to scale: the title block box (sheet outline), the usable area
        /// (dashed green), and every ticked view with its name. Views that do not fit are red
        /// and sit below the sheet, where Run puts them.
        /// </summary>
        private void UpdatePreview()
        {
            PreviewCanvas.Children.Clear();

            var placements = _viewModel.PreviewPack();
            var region = _viewModel.Region;
            PreviewEmptyText.Visibility = region != null && placements.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            PreviewEmptyText.Text = region == null
                ? "No single title block found on this sheet."
                : "Tick at least one view to see the layout.";
            if (region == null)
                return;

            double canvasW = PreviewCanvas.ActualWidth, canvasH = PreviewCanvas.ActualHeight;
            if (canvasW < 20 || canvasH < 20)
                return;

            const double MmPerFoot = 304.8;
            var usable = region.GetOverallBounds();
            var sheet = _viewModel.TitleBlockRect ?? usable;

            // Extents to fit: the sheet, plus any overflow views below it.
            double minX = sheet.Min.X, maxX = sheet.Max.X, minY = sheet.Min.Y, maxY = sheet.Max.Y;
            foreach (var p in placements)
            {
                double halfW = p.Item.WidthMm / MmPerFoot / 2.0, halfH = p.Item.HeightMm / MmPerFoot / 2.0;
                minX = System.Math.Min(minX, p.NewCenter.X - halfW);
                maxX = System.Math.Max(maxX, p.NewCenter.X + halfW);
                minY = System.Math.Min(minY, p.NewCenter.Y - halfH);
                maxY = System.Math.Max(maxY, p.NewCenter.Y + halfH);
            }

            double spanX = System.Math.Max(maxX - minX, 1e-6), spanY = System.Math.Max(maxY - minY, 1e-6);
            double scale = System.Math.Min(canvasW / spanX, canvasH / spanY);
            double offsetX = (canvasW - spanX * scale) / 2.0, offsetY = (canvasH - spanY * scale) / 2.0;

            double Px(double xFeet) => offsetX + (xFeet - minX) * scale;
            double Py(double yFeet) => offsetY + (maxY - yFeet) * scale; // sheet Y is up, canvas Y is down

            void AddRect(double left, double top, double right, double bottom, Brush fill, Brush stroke, double thickness, bool dashed, double radius)
            {
                var rect = new Rectangle
                {
                    Width = System.Math.Max(Px(right) - Px(left), 2),
                    Height = System.Math.Max(Py(bottom) - Py(top), 2),
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = thickness,
                    RadiusX = radius,
                    RadiusY = radius
                };
                if (dashed)
                    rect.StrokeDashArray = new DoubleCollection { 5, 3 };
                Canvas.SetLeft(rect, Px(left));
                Canvas.SetTop(rect, Py(top));
                PreviewCanvas.Children.Add(rect);
            }

            AddRect(sheet.Min.X, sheet.Max.Y, sheet.Max.X, sheet.Min.Y, SheetFill, SheetStroke, 1, false, 0);
            AddRect(usable.Min.X, usable.Max.Y, usable.Max.X, usable.Min.Y, Brushes.Transparent, UsableStroke, 1.2, true, 0);

            foreach (var p in placements)
            {
                double halfW = p.Item.WidthMm / MmPerFoot / 2.0, halfH = p.Item.HeightMm / MmPerFoot / 2.0;
                double left = p.NewCenter.X - halfW, right = p.NewCenter.X + halfW;
                double top = p.NewCenter.Y + halfH, bottom = p.NewCenter.Y - halfH;
                AddRect(left, top, right, bottom, p.Fits ? FitFill : OverflowFill, p.Fits ? FitStroke : OverflowStroke, 1.2, false, 2);

                // Name inside the box, when the box is big enough to read it.
                double wPx = Px(right) - Px(left), hPx = Py(bottom) - Py(top);
                if (wPx >= 34 && hPx >= 16)
                {
                    var label = new TextBlock
                    {
                        Text = p.Item.ViewName,
                        Width = wPx - 6,
                        MaxHeight = hPx - 4,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = p.Fits ? NameText : OverflowStroke,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = hPx >= 34 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        IsHitTestVisible = false
                    };
                    label.Measure(new Size(wPx - 6, hPx - 4));
                    Canvas.SetLeft(label, Px(left) + 3);
                    Canvas.SetTop(label, Py(top) + System.Math.Max(2, (hPx - label.DesiredSize.Height) / 2.0));
                    PreviewCanvas.Children.Add(label);
                }
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
