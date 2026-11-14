using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.Converters;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.UI.Views
{
    /// <summary>
    /// Sheet Views Combine window. Draws the to-scale preview of the planned sheet on a canvas,
    /// shows the result dialog after a run, and keeps Revit's last request from being orphaned
    /// by closing mid-run. Everything else is bound.
    /// </summary>
    public partial class SheetViewsCombineWindow : Window
    {
        private static readonly Brush SheetFill = Brushes.White;
        private static readonly Brush FrameStroke = SheetColors.Frozen(0x8F, 0xA3, 0xB8);
        private static readonly Brush AreaStroke = SheetColors.Frozen(0x5B, 0x71, 0x85);
        private static readonly Brush FixedFill = SheetColors.Frozen(0xE1, 0xE6, 0xEC);
        private static readonly Brush LabelBrush = SheetColors.Frozen(0x1A, 0x2B, 0x3C);
        private static readonly Brush StayBrush = MakeHatch();

        private readonly SheetViewsCombineViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public SheetViewsCombineWindow(SheetViewsCombineViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _viewModel.OwnerWindow = this;
            DataContext = _viewModel;
            Title = ToolCatalog.SheetViewsCombine.Title;

            _viewModel.PlanChanged += OnPlanChanged;
            _viewModel.RunFinished += OnRunFinished;
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;
            Closed += (_, _) =>
            {
                _viewModel.PlanChanged -= OnPlanChanged;
                _viewModel.RunFinished -= OnRunFinished;
                _viewModel.Dispose();
            };
            Loaded += (_, _) => DrawPreview();
        }

        /// <summary>A request still running in Revit would call back into a closed window: wait for it.</summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_viewModel.IsBusy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Revit is still working on the last request. Close the window when it has finished.",
                    ToolCatalog.SheetViewsCombine.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _viewModel.SaveSettings();
        }

        /// <summary>The result dialog: a summary; full details stay in step 5 and the log.</summary>
        private void OnRunFinished(object sender, RunFinishedEventArgs e)
        {
            MessageBox.Show(this, e.Message, ToolCatalog.SheetViewsCombine.Title, MessageBoxButton.OK,
                e.HasProblems ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        /// <summary>Esc closes the window; Enter in a text box commits it.</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_viewModel.IsBusy)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox box)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }

        private void OnPlanChanged(object sender, EventArgs e) => DrawPreview();

        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawPreview();

        /// <summary>
        /// Draws the title block frame, the usable area (dashed), fixed items, and the views of the
        /// sheet shown, coloured by the sheet they come from. Sheet Y points up, canvas Y down.
        /// </summary>
        private void DrawPreview()
        {
            PreviewCanvas.Children.Clear();
            var plan = _viewModel.Plan;
            double canvasW = PreviewCanvas.ActualWidth, canvasH = PreviewCanvas.ActualHeight;
            if (plan == null || canvasW < 20 || canvasH < 20)
                return;

            var sheet = _viewModel.CurrentSheet;
            SheetRect? world = sheet?.Frame ?? sheet?.Area;
            if (sheet != null)
            {
                foreach (var v in sheet.Views)
                    if (v.Target is SheetRect t)
                        world = world?.Union(t) ?? t;
                foreach (var f in sheet.FixedItems)
                    world = world?.Union(f) ?? f;
            }
            if (world is not SheetRect w || !w.IsValid)
            {
                AddText("No preview yet — " + (plan.Blocker ?? "nothing to show."), canvasW / 2, canvasH / 2, canvasW - 40, 12, centre: true, LabelBrush);
                return;
            }

            const double pad = 10;
            double scale = Math.Min((canvasW - 2 * pad) / w.Width, (canvasH - 2 * pad) / w.Height);
            double offX = (canvasW - w.Width * scale) / 2;
            double offY = (canvasH - w.Height * scale) / 2;

            Rect ToCanvas(SheetRect r) => new(
                offX + (r.MinX - w.MinX) * scale,
                offY + (w.MaxY - r.MaxY) * scale,
                Math.Max(1, r.Width * scale),
                Math.Max(1, r.Height * scale));

            if (sheet.Frame is SheetRect frame)
                AddRect(ToCanvas(frame), SheetFill, FrameStroke, 1, null);
            if (sheet.Area is SheetRect area)
                AddRect(ToCanvas(area), null, AreaStroke, 1, new DoubleCollection { 4, 3 });
            foreach (var f in sheet.FixedItems)
                AddRect(ToCanvas(f), FixedFill, FrameStroke, 1, null);

            foreach (var v in sheet.Views)
            {
                if (v.Target is not SheetRect target)
                    continue;
                var r = ToCanvas(target);
                Brush stroke, fill, text;
                DoubleCollection dash = null;
                switch (v.Action)
                {
                    case ViewAction.StaysInPlace:
                    case ViewAction.Skip:
                        fill = StayBrush; stroke = FrameStroke; text = AreaStroke;
                        break;
                    case ViewAction.CopyLegend:
                        fill = SheetFill; stroke = SheetColors.Strong(3); text = SheetColors.Strong(3);
                        dash = new DoubleCollection { 5, 3 };
                        break;
                    default:
                        fill = SheetColors.Soft(v.Slot); stroke = SheetColors.Strong(v.Slot); text = SheetColors.Strong(v.Slot);
                        break;
                }
                AddRect(r, fill, stroke, 1.4, dash);

                if (r.Width >= 18 && r.Height >= 14)
                {
                    double size = Math.Max(8, Math.Min(12, r.Height / 4));
                    string number = v.DetailNumber ?? v.View.DetailNumber;
                    AddText(number, r.Left + r.Width / 2, r.Top + r.Height * 0.4, r.Width - 4, size + 2, centre: true, text, bold: true);
                    if (r.Height >= 34)
                        AddText(v.View.Name, r.Left + r.Width / 2, r.Top + r.Height * 0.4 + size + 4, r.Width - 6, Math.Max(8, size - 1), centre: true, text);
                }
            }
        }

        private void AddRect(Rect r, Brush fill, Brush stroke, double thickness, DoubleCollection dash)
        {
            var rect = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeDashArray = dash
            };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            PreviewCanvas.Children.Add(rect);
        }

        private void AddText(string text, double centreX, double top, double width, double fontSize, bool centre, Brush brush, bool bold = false)
        {
            var block = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = brush,
                Width = Math.Max(10, width),
                TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(block, centreX - block.Width / 2);
            Canvas.SetTop(block, top - fontSize * 0.7);
            PreviewCanvas.Children.Add(block);
        }

        private static Brush MakeHatch()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(SheetColors.Frozen(0xEC, 0xEF, 0xF3), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(null, new Pen(SheetColors.Frozen(0xB5, 0xC2, 0xCF), 2), new LineGeometry(new Point(0, 8), new Point(8, 0))));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
            => LogClipboardService.CopySelected(LogListBox.SelectedItems);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
