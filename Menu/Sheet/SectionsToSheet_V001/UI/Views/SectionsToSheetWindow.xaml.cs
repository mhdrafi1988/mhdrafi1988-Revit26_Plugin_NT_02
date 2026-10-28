using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.UI.Views
{
    /// <summary>
    /// Sections To Sheet window. Draws the to-scale preview of the current new sheet on a canvas,
    /// steps aside while the user picks markers in the view, and keeps Revit's last request from
    /// being orphaned by closing mid-run. Everything else is bound.
    /// </summary>
    public partial class SectionsToSheetWindow : Window
    {
        private static readonly Brush SheetFill = Brushes.White;
        private static readonly Brush FrameStroke = Frozen(0x8F, 0xA3, 0xB8);
        private static readonly Brush AreaStroke = Frozen(0x5B, 0x71, 0x85);
        private static readonly Brush NewFill = Frozen(0xEA, 0xF1, 0xF8);
        private static readonly Brush NewStroke = Frozen(0x2D, 0x6C, 0xDF);
        private static readonly Brush MoveFill = Frozen(0xFD, 0xF1, 0xE1);
        private static readonly Brush MoveStroke = Frozen(0xC9, 0x8A, 0x2E);
        private static readonly Brush LabelBrush = Frozen(0x1A, 0x2B, 0x3C);

        private readonly SectionsToSheetViewModel _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>.</summary>
        public SectionsToSheetWindow(SectionsToSheetViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;
            Title = ToolCatalog.SectionsToSheet.Title;

            _viewModel.PlanChanged += OnPlanChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;
            Closed += (_, _) =>
            {
                _viewModel.PlanChanged -= OnPlanChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
                    ToolCatalog.SectionsToSheet.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _viewModel.SaveSettings();
        }

        /// <summary>The window hides while the user picks markers in the view, and comes back afterwards.</summary>
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SectionsToSheetViewModel.IsPicking))
                return;
            if (_viewModel.IsPicking)
            {
                Hide();
            }
            else if (!IsVisible)
            {
                Show();
                Activate();
            }
        }

        /// <summary>Esc closes the confirm card, or the window; Enter in a number box commits it.</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_viewModel.IsConfirmOpen)
                    _viewModel.CancelConfirmCommand.Execute(null);
                else
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
        /// Draws the title block frame, the usable area (dashed) and the views of the sheet shown,
        /// blue when new, orange when they come off another sheet. Sheet Y points up, canvas Y down.
        /// </summary>
        private void DrawPreview()
        {
            PreviewCanvas.Children.Clear();
            var plan = _viewModel.Plan;
            double canvasW = PreviewCanvas.ActualWidth, canvasH = PreviewCanvas.ActualHeight;
            if (plan == null || canvasW < 20 || canvasH < 20)
                return;

            var sheet = _viewModel.CurrentSheet;
            LayoutRect? world = plan.Frame ?? plan.Area;
            if (sheet != null)
                foreach (var v in sheet.Viewports)
                    world = world?.Union(v.Target) ?? v.Target;
            if (world is not LayoutRect w || !w.IsValid)
            {
                AddText("No preview yet — " + (plan.Blocker ?? "nothing to show."), canvasW / 2, canvasH / 2, canvasW - 40, 12, centre: true);
                return;
            }

            const double pad = 10;
            double scale = Math.Min((canvasW - 2 * pad) / w.Width, (canvasH - 2 * pad) / w.Height);
            double offX = (canvasW - w.Width * scale) / 2;
            double offY = (canvasH - w.Height * scale) / 2;

            Rect ToCanvas(LayoutRect r) => new(
                offX + (r.MinX - w.MinX) * scale,
                offY + (w.MaxY - r.MaxY) * scale,
                Math.Max(1, r.Width * scale),
                Math.Max(1, r.Height * scale));

            if (plan.Frame is LayoutRect frame)
                AddRect(ToCanvas(frame), SheetFill, FrameStroke, 1, null);
            if (plan.Area is LayoutRect area)
                AddRect(ToCanvas(area), null, AreaStroke, 1, new DoubleCollection { 4, 3 });

            if (sheet == null)
                return;

            foreach (var v in sheet.Viewports)
            {
                var r = ToCanvas(v.Target);
                AddRect(r, v.IsMove ? MoveFill : NewFill, v.IsMove ? MoveStroke : NewStroke, 1.2, null);

                // Title line along the bottom, as on the printed sheet.
                double lineY = r.Bottom - Math.Min(10, r.Height * 0.18);
                var line = new Line { X1 = r.Left + 2, X2 = r.Right - 2, Y1 = lineY, Y2 = lineY, Stroke = LabelBrush, StrokeThickness = 0.8 };
                PreviewCanvas.Children.Add(line);

                if (r.Width >= 18 && r.Height >= 14)
                {
                    double size = Math.Max(8, Math.Min(12, r.Height / 4));
                    AddText(v.DetailNumber.ToString(), r.Left + r.Width / 2, r.Top + r.Height * 0.42, r.Width - 4, size + 2, centre: true, bold: true);
                    if (r.Height >= 34)
                        AddText(v.Section.Name, r.Left + r.Width / 2, r.Top + r.Height * 0.42 + size + 4, r.Width - 6, Math.Max(8, size - 1), centre: true);
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

        private void AddText(string text, double centreX, double top, double width, double fontSize, bool centre, bool bold = false)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = LabelBrush,
                Width = Math.Max(10, width),
                TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(block, centreX - block.Width / 2);
            Canvas.SetTop(block, top - fontSize * 0.7);
            PreviewCanvas.Children.Add(block);
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
            => LogClipboardService.CopySelected(LogListBox.SelectedItems);

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
