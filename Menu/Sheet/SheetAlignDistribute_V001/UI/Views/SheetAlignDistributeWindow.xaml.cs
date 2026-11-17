using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
// Autodesk.Revit.DB also has a Rectangle; alias the WPF one so the preview code is unambiguous.
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.Views
{
    /// <summary>
    /// The Sheet Align &amp; Distribute window. Everything the user changes lives in
    /// <see cref="ViewModel_SheetAlignDistribute"/>; the code-behind only draws the sheet preview,
    /// which is a Canvas rather than bound shapes because the shapes change on every re-plan.
    /// </summary>
    public partial class SheetAlignDistributeWindow : Window
    {
        private readonly ViewModel_SheetAlignDistribute _viewModel;

        /// <summary>Creates the window for <paramref name="viewModel"/>'s sheet.</summary>
        /// <param name="viewModel">The view model, already holding the session.</param>
        /// <param name="sheetId">The sheet the tool was opened on, so a second click can find this window.</param>
        public SheetAlignDistributeWindow(ViewModel_SheetAlignDistribute viewModel, ElementId sheetId)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            SheetId = sheetId;

            InitializeComponent();
            DataContext = _viewModel;
            _viewModel.OwnerWindow = this;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            PreviewCanvas.SizeChanged += (_, _) => DrawPreview();
            Loaded += (_, _) => DrawPreview();
        }

        /// <summary>The sheet this window was opened on.</summary>
        public ElementId SheetId { get; }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel_SheetAlignDistribute.Plan))
                DrawPreview();
        }

        /// <summary>Copies the log lines the user selected (the ListBox's selection is not bindable).</summary>
        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectedLogEntries = LogListBox.SelectedItems.Cast<LogEntry>().ToList();
            _viewModel.CopySelectedLogCommand.Execute(null);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Saves the settings and releases the Revit ExternalEvent when the window closes.</summary>
        protected override void OnClosed(EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            try { _viewModel.Save(); }
            catch (Exception) { /* a settings file that cannot be written must not block the close */ }
            _viewModel.Dispose();
            base.OnClosed(e);
        }

        // ── Preview ─────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the sheet to scale: the title block, the margins, where each element sits now and
        /// where the plan puts it. Drawn at true scale on purpose — a 3 mm correction on an A1 sheet
        /// really is a hairline, and showing it any larger would misrepresent the change.
        /// </summary>
        private void DrawPreview()
        {
            PreviewCanvas.Children.Clear();

            double width = PreviewCanvas.ActualWidth;
            double height = PreviewCanvas.ActualHeight;
            if (width < 20 || height < 20) return;

            AlignPlan plan = _viewModel.Plan;
            if (plan?.Snapshot?.TitleBlockFrame is not LayoutRect frame || !frame.IsValid)
            {
                AddMessage(width, height, plan?.Blocker ?? "No title block on this sheet, so there is nothing to draw.");
                return;
            }

            const double pad = 6;
            double scale = Math.Min((width - 2 * pad) / frame.Width, (height - 2 * pad) / frame.Height);
            if (scale <= 0 || double.IsInfinity(scale)) return;

            double offsetX = (width - frame.Width * scale) / 2.0;
            double offsetY = (height - frame.Height * scale) / 2.0;

            // Sheet space has Y up, the canvas has Y down, so Y is flipped here once.
            double Px(double feetX) => offsetX + (feetX - frame.MinX) * scale;
            double Py(double feetY) => height - offsetY - (feetY - frame.MinY) * scale;

            void Place(Shape shape, LayoutRect rect)
            {
                shape.Width = Math.Max(1, rect.Width * scale);
                shape.Height = Math.Max(1, rect.Height * scale);
                Canvas.SetLeft(shape, Px(rect.MinX));
                Canvas.SetTop(shape, Py(rect.MaxY));
                PreviewCanvas.Children.Add(shape);
            }

            // The title block, then the usable area the margins leave.
            Place(new WpfRectangle
            {
                Stroke = Brush("BrushBorderInput"),
                StrokeThickness = 1.2,
                Fill = Brush("BrushSurfaceCard")
            }, frame);

            if (plan.Usable is LayoutRect usable && usable.IsValid)
                Place(new WpfRectangle
                {
                    Stroke = Brush("BrushStatusInfo"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 5, 4 },
                    Fill = Brushes.Transparent,
                    Opacity = 0.7
                }, usable);

            // Where each element sits now, for the ones that are about to move.
            foreach (var move in plan.Moves.Where(m => m.NeedsMove))
                Place(new WpfRectangle
                {
                    Stroke = Brush("BrushTextTertiary"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = Brushes.Transparent
                }, move.Element.Footprint);

            // Where the plan puts each element.
            foreach (var move in plan.Moves)
            {
                (string fill, string edge) = ColoursFor(move.Status);
                Place(new WpfRectangle
                {
                    Stroke = Brush(edge),
                    StrokeThickness = 1.4,
                    Fill = Brush(fill),
                    Opacity = 0.9,
                    ToolTip = move.Element.Name + " — " + move.StatusLabel
                }, move.Target);

                AddLabel(move, Px, Py, scale);
            }
        }

        /// <summary>The detail number, or the start of the view name, when the box has room for it.</summary>
        private void AddLabel(AlignMove move, Func<double, double> px, Func<double, double> py, double scale)
        {
            double boxWidth = move.Target.Width * scale;
            double boxHeight = move.Target.Height * scale;
            if (boxWidth < 26 || boxHeight < 16) return;

            string text = string.IsNullOrWhiteSpace(move.Element.DetailNumber)
                ? move.Element.Name
                : move.Element.DetailNumber;

            var label = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("BrushTextSecondary"),
                MaxWidth = Math.Max(20, boxWidth - 6),
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, px(move.Target.MinX) + 3);
            Canvas.SetTop(label, py(move.Target.MaxY) + 2);
            PreviewCanvas.Children.Add(label);
        }

        private void AddMessage(double width, double height, string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(80, width - 24),
                TextAlignment = TextAlignment.Center,
                Foreground = Brush("BrushTextSecondary")
            };
            label.Measure(new Size(label.MaxWidth, height));
            Canvas.SetLeft(label, (width - label.DesiredSize.Width) / 2.0);
            Canvas.SetTop(label, (height - label.DesiredSize.Height) / 2.0);
            PreviewCanvas.Children.Add(label);
        }

        /// <summary>The fill and outline brush keys for a status, taken from the shared status tokens.</summary>
        private static (string Fill, string Edge) ColoursFor(AlignStatus status) => status switch
        {
            AlignStatus.Move => ("BrushStatusInfoSoft", "BrushStatusInfo"),
            AlignStatus.InPlace => ("BrushStatusOkSoft", "BrushStatusOk"),
            AlignStatus.OverLimit => ("BrushStatusWarnSoft", "BrushStatusWarn"),
            AlignStatus.Blocked => ("BrushStatusErrorSoft", "BrushStatusError"),
            AlignStatus.NoPartner => ("BrushStatusWarnSoft", "BrushStatusWarnBorder"),
            _ => ("BrushSystemSecondaryBackground", "BrushTextDisabled")
        };

        private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;
    }
}
