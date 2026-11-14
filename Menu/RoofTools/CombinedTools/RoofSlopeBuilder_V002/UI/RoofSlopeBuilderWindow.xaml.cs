// =======================================================
// File: RoofSlopeBuilderWindow.xaml.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V002.UI
// Options window. Fills its boxes from the saved settings, checks every
// number on Apply (and names the box that is wrong) and writes the
// result back into the RoofSlopeSettings it was given.
// =======================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Infrastructure;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V002.UI
{
    /// <summary>One roof in the "Selected roofs" list.</summary>
    public class RoofRow
    {
        /// <summary>The roof behind this row.</summary>
        public RoofModel Model;
        /// <summary>Called after the tick changes (the binding has already written the value).</summary>
        public Action Changed;
        /// <summary>Whether the roof is processed.</summary>
        public bool Use { get { return Model.Use; } set { if (Model.Use == value) return; Model.Use = value; Changed?.Invoke(); } }
        /// <summary>Roof name and id.</summary>
        public string Name { get { return Model.Name; } }
        /// <summary>Area and number of openings.</summary>
        public string Info
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0:0.#} m2 - {1} openings", Model.AreaM2, Model.Input.Drains.Count); }
        }
        /// <summary>Warning shown under the roof, or null.</summary>
        public string Note { get { return Model.Note; } }
        /// <summary>Collapses the note line when there is no note.</summary>
        public Visibility NoteVisibility { get { return string.IsNullOrEmpty(Model.Note) ? Visibility.Collapsed : Visibility.Visible; } }
    }

    /// <summary>One opening in the drains grid.</summary>
    public class DrainRow
    {
        /// <summary>The opening behind this row.</summary>
        public DrainInfo Drain;
        /// <summary>Called after the tick changes.</summary>
        public Action Changed;
        /// <summary>Whether the opening is used as a drain.</summary>
        public bool Use { get { return Drain.Selected; } set { if (Drain.Selected == value) return; Drain.Selected = value; Changed?.Invoke(); } }
        /// <summary>Roof name.</summary>
        public string Roof { get; set; }
        /// <summary>"Opening n", smallest first.</summary>
        public string Opening { get; set; }
        /// <summary>Area in m2.</summary>
        public string Size { get; set; }
        /// <summary>Plan position of the opening's centre, in metres from the project origin.</summary>
        public string Position { get; set; }
    }

    /// <summary>Options window of Roof Slope Builder.</summary>
    public partial class RoofSlopeBuilderWindow : Window
    {
        private const double MmToFt = 1 / 304.8;
        private const double FtToMm = 304.8;
        private const double M2ToFt2 = 10.7639104;

        private readonly List<RoofModel> _roofs;
        private readonly RoofSlopeSettings _settings;
        private readonly ObservableCollection<DrainRow> _drainRows = new ObservableCollection<DrainRow>();
        private bool _ready;
        private readonly bool _viewIsPlan;

        /// <summary>V002: true when the user pressed "Find ridges only" (draw the ridges, leave the roofs as they are).</summary>
        public bool FindRidgesOnly { get; private set; }

        /// <summary>
        /// Creates the window for <paramref name="roofs"/>, filled from <paramref name="settings"/>.
        /// <paramref name="skipped"/> lists roofs that were left out; <paramref name="settingsWarning"/>
        /// says when the saved settings could not be read. Both may be empty or null.
        /// <paramref name="lineStyles"/> and <paramref name="familyTypes"/> fill the Ridges tab;
        /// <paramref name="viewIsPlan"/> says whether ridges can be drawn in the active view.
        /// </summary>
        public RoofSlopeBuilderWindow(List<RoofModel> roofs, RoofSlopeSettings settings, string skipped, string settingsWarning,
                                      List<string> lineStyles, List<string> familyTypes, bool viewIsPlan)
        {
            _viewIsPlan = viewIsPlan;
            _roofs = roofs;
            _settings = settings;
            InitializeComponent();
            Title = ToolCatalog.RoofSlopeBuilderV002.Title;

            string notice = ((skipped ?? "").Trim() + "\n" + (settingsWarning ?? "")).Trim();
            if (notice.Length > 0)
            {
                TxtNotice.Text = "Not processed:\n" + notice;
                BdrNotice.Visibility = Visibility.Visible;
            }

            CmbLineStyle.ItemsSource = lineStyles;
            CmbFamilyType.ItemsSource = familyTypes;
            TxtNoFamilies.Visibility = familyTypes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BdrNotPlan.Visibility = viewIsPlan ? Visibility.Collapsed : Visibility.Visible;
            BtnFindRidges.IsEnabled = viewIsPlan;
            LoadToUi(settings);
            RefreshRoofList();
            GridDrains.ItemsSource = _drainRows;
            _ready = true;

            RebuildDrainRows();
            UpdateSlopeInfo();
            PnlSaddle.Visibility = CmbRidgeMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------ settings <-> boxes

        private static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        private void LoadToUi(RoofSlopeSettings s)
        {
            ChkInner.IsChecked = s.IncludeInnerLoops;
            TxtInnerArea.Text = F(s.InnerMinArea / M2ToFt2);
            ChkPerp.IsChecked = s.AddPerpendicular;
            ChkPerpEdge.IsChecked = s.PerpToBoundary;
            ChkPerpRidge.IsChecked = s.PerpToRidge;

            ChkRidge.IsChecked = s.AddDrainRidges;
            CmbRidgeMode.SelectedIndex = s.RidgeMode == RidgeMode.Saddle ? 1 : 0;
            TxtRidgeSpacing.Text = F(s.RidgeSpacing * FtToMm);
            ChkSplit.IsChecked = s.AddSplitLines;
            int best = 1; double bestDiff = double.MaxValue;
            for (int i = 0; i < CmbSaddleRatio.Items.Count; i++)
            {
                double r = double.Parse((string)((ComboBoxItem)CmbSaddleRatio.Items[i]).Tag, CultureInfo.InvariantCulture);
                if (Math.Abs(r - s.SaddleWidthRatio) < bestDiff) { bestDiff = Math.Abs(r - s.SaddleWidthRatio); best = i; }
            }
            CmbSaddleRatio.SelectedIndex = best;
            TxtSaddleFactor.Text = F(s.SaddleSlopeFactor);
            TxtMaxSpacing.Text = F(s.MaxDrainSpacing * FtToMm);

            ChkCurve.IsChecked = s.AddCurvePoints;
            TxtCurveCount.Text = s.CurvePointCount.ToString(CultureInfo.InvariantCulture);
            TxtCurveBig.Text = F(s.LargeCurveLength * FtToMm);
            ChkCurveMid.IsChecked = s.SmallCurveMidpoint;
            TxtCurveGap.Text = F(s.MinGap * FtToMm);
            CmbGapMode.SelectedIndex = (int)s.GapMode;

            ChkLowest.IsChecked = s.LowestSizeOnly;
            TxtDrainMax.Text = F(s.DrainMaxArea / M2ToFt2);

            CmbSlopeUnit.SelectedIndex = s.SlopeUnitIndex;
            switch (s.SlopeUnitIndex)
            {
                case 1: TxtSlope.Text = F(1 / s.Slope); break;
                case 2: TxtSlope.Text = F(s.Slope * 1000); break;
                default: TxtSlope.Text = F(s.Slope * 100); break;
            }
            TxtMinFall.Text = F(s.MinFallN);
            TxtMaxHeight.Text = F(s.MaxHeightWarn * FtToMm);

            ChkReset.IsChecked = s.ResetShape;
            TxtMerge.Text = F(s.MergeTolerance * FtToMm);
            TxtRound.Text = F(s.RoundHeightTo * FtToMm);

            ChkDrawRidges.IsChecked = s.DrawRidgeLines;
            RbFamily.IsChecked = s.RidgeLineKind == RidgeLineKind.DetailFamily;
            RbDetailLine.IsChecked = s.RidgeLineKind != RidgeLineKind.DetailFamily;
            List<string> styles = (List<string>)CmbLineStyle.ItemsSource;
            CmbLineStyle.SelectedItem = styles.Contains(s.RidgeLineStyle) ? s.RidgeLineStyle
                : styles.Contains("Thin Lines") ? "Thin Lines" : styles.FirstOrDefault();
            List<string> fams = (List<string>)CmbFamilyType.ItemsSource;
            CmbFamilyType.SelectedItem = fams.Contains(s.RidgeFamilyType) ? s.RidgeFamilyType : fams.FirstOrDefault();
            ChkMarkPoints.IsChecked = s.MarkRidgePoints;
            TxtPointDia.Text = F(s.RidgePointDiameter * FtToMm);
        }

        /// <summary>Parses a box. Accepts "1.5" and "1,5"; refuses "1,500" style thousands separators.</summary>
        private static bool TryNum(TextBox box, out double v)
        {
            string s = (box.Text ?? "").Trim();
            if (s.Contains(',') && s.Contains('.')) { v = 0; return false; }
            s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        /// <summary>Reads a box that must be within [min, max]; adds a message to <paramref name="errors"/> otherwise.</summary>
        private static double Read(TextBox box, string label, double min, double max, string unit, List<string> errors)
        {
            if (!TryNum(box, out double v))
            {
                errors.Add(label + ": \"" + box.Text + "\" is not a number.");
                return min;
            }
            if (v < min || v > max)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0}: enter {1} to {2} {3}.", label, F(min), F(max), unit).TrimEnd() );
                return min;
            }
            return v;
        }

        /// <summary>Slope as a fraction (0.015 = 1.5 %), or 0 when the input is not usable.</summary>
        private double SlopeFraction()
        {
            if (!TryNum(TxtSlope, out double v) || v <= 0) return 0;
            switch (CmbSlopeUnit.SelectedIndex)
            {
                case 1: return 1 / v;        // 1 : n
                case 2: return v / 1000;     // mm per m
                default: return v / 100;     // %
            }
        }

        // ------------------------------------------------------------ lists

        private void RefreshRoofList()
        {
            LstRoofs.ItemsSource = _roofs.Select(r => new RoofRow { Model = r, Changed = RebuildDrainRows }).ToList();
        }

        private void RebuildDrainRows()
        {
            if (!_ready) return;
            _drainRows.Clear();
            foreach (RoofModel m in _roofs.Where(r => r.Use))
            {
                int n = 1;
                foreach (DrainInfo d in m.Input.Drains.OrderBy(x => x.Area))
                    _drainRows.Add(new DrainRow
                    {
                        Drain = d,
                        Changed = UpdateSummary,
                        Roof = m.Name,
                        Opening = "Opening " + n++,
                        Size = (d.Area / M2ToFt2).ToString("0.000", CultureInfo.InvariantCulture),
                        Position = string.Format(CultureInfo.InvariantCulture, "{0:0.00}, {1:0.00}", d.Center.X * 0.3048, d.Center.Y * 0.3048)
                    });
            }
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (!_ready) return;
            List<RoofModel> used = _roofs.Where(r => r.Use).ToList();
            int drains = used.Sum(r => r.Input.Drains.Count(d => d.Selected));
            int noDrain = used.Count(r => !r.Input.Drains.Any(d => d.Selected));
            TxtSummary.Text = string.Format("{0} roofs, {1} drains selected", used.Count, drains)
                + (noDrain > 0 ? "   -   " + noDrain + " roof(s) have no drain and will be skipped" : "");
            BtnApply.IsEnabled = used.Count > 0 && drains > 0;
            BtnFindRidges.IsEnabled = _viewIsPlan && BtnApply.IsEnabled;
        }

        private void UpdateSlopeInfo()
        {
            if (!_ready) return;
            double s = SlopeFraction();
            TxtSlopeInfo.Text = s > 0
                ? string.Format(CultureInfo.InvariantCulture, "  = {0:0.##} %  =  1:{1:0}  =  {2:0.#} mm per metre", s * 100, 1 / s, s * 1000)
                : "  enter a slope greater than 0";
        }

        // ------------------------------------------------------------ events

        private void Slope_Changed(object sender, EventArgs e) { UpdateSlopeInfo(); }

        private void RidgeMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            PnlSaddle.Visibility = CmbRidgeMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnAllRoofs_Click(object sender, RoutedEventArgs e) { SetAllRoofs(true); }
        private void BtnNoRoofs_Click(object sender, RoutedEventArgs e) { SetAllRoofs(false); }

        private void SetAllRoofs(bool use)
        {
            foreach (RoofModel m in _roofs) m.Use = use;
            RefreshRoofList();
            RebuildDrainRows();
        }

        private void BtnRedetect_Click(object sender, RoutedEventArgs e)
        {
            List<string> errors = new List<string>();
            double max = Read(TxtDrainMax, "Max drain opening size", 0.0001, 100, "m2", errors);
            if (errors.Count > 0) { ShowErrors(errors); return; }
            _settings.DrainMaxArea = max * M2ToFt2;
            _settings.LowestSizeOnly = ChkLowest.IsChecked == true;
            foreach (RoofModel m in _roofs)
                DrainDetector.ApplyDefaultSelection(m.Input.Drains, _settings);
            RebuildDrainRows();
        }

        private void ShowErrors(List<string> errors)
        {
            MessageBox.Show(this, "Please correct:\n\n- " + string.Join("\n- ", errors), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (!ReadAll(false)) return;
            FindRidgesOnly = false;
            DialogResult = true;
        }

        private void BtnFindRidges_Click(object sender, RoutedEventArgs e)
        {
            if (!ReadAll(true)) return;
            FindRidgesOnly = true;
            DialogResult = true;
        }

        /// <summary>Checks every box and writes the settings. False (after showing what is wrong) when something is invalid.</summary>
        private bool ReadAll(bool findOnly)
        {
            List<string> errors = new List<string>();

            bool drawLines = ChkDrawRidges.IsChecked == true, markPoints = ChkMarkPoints.IsChecked == true;
            double pointDia = Read(TxtPointDia, "Ridge point circle diameter", 10, 3000, "mm", errors);
            if (findOnly && !drawLines && !markPoints)
                errors.Add("Find ridges only: tick \"Draw ridge lines\" or \"Mark ridge points\" on the Ridges tab.");
            if (drawLines && RbFamily.IsChecked == true && CmbFamilyType.SelectedItem == null)
                errors.Add("Ridge lines: choose a line-based detail family, or draw them as detail lines.");

            double slope = SlopeFraction();
            if (slope <= 0 || slope > 0.5) errors.Add("Slope: enter a slope between 0 and 50 %.");

            double innerArea = Read(TxtInnerArea, "Min opening area", 0, 1000, "m2", errors);
            double ridgeSpacing = Read(TxtRidgeSpacing, "Point spacing along ridge", 100, 100000, "mm", errors);
            double saddleFactor = Read(TxtSaddleFactor, "Saddle slope", 1, 10, "x", errors);
            double maxSpacing = Read(TxtMaxSpacing, "Max drain spacing", 100, 1000000, "mm", errors);
            double curveCount = Read(TxtCurveCount, "Points per large curve", 0, 100, "", errors);
            double curveBig = Read(TxtCurveBig, "Large curve is longer than", 0, 1000000, "mm", errors);
            double curveGap = Read(TxtCurveGap, "Minimum gap", 0, 100000, "mm", errors);
            double drainMax = Read(TxtDrainMax, "Max drain opening size", 0.0001, 100, "m2", errors);
            double minFall = Read(TxtMinFall, "Minimum finished fall", 1, 10000, "", errors);
            double maxHeight = Read(TxtMaxHeight, "Max height", 0, 100000, "mm", errors);
            double merge = Read(TxtMerge, "Never place points closer than", 10, 10000, "mm", errors);
            double round = Read(TxtRound, "Round point heights to", 0, 100, "mm", errors);

            if (errors.Count > 0) { ShowErrors(errors); return false; }

            RoofSlopeSettings s = _settings;

            s.DrawRidgeLines = drawLines;
            s.RidgeLineKind = RbFamily.IsChecked == true ? RidgeLineKind.DetailFamily : RidgeLineKind.DetailLine;
            s.RidgeLineStyle = CmbLineStyle.SelectedItem as string ?? "Thin Lines";
            s.RidgeFamilyType = CmbFamilyType.SelectedItem as string ?? "";
            s.MarkRidgePoints = markPoints;
            s.RidgePointDiameter = pointDia * MmToFt;

            s.IncludeInnerLoops = ChkInner.IsChecked == true;
            s.InnerMinArea = innerArea * M2ToFt2;

            s.AddPerpendicular = ChkPerp.IsChecked == true;
            s.PerpToBoundary = ChkPerpEdge.IsChecked == true;
            s.PerpToRidge = ChkPerpRidge.IsChecked == true;

            s.AddDrainRidges = ChkRidge.IsChecked == true;
            s.RidgeMode = CmbRidgeMode.SelectedIndex == 1 ? RidgeMode.Saddle : RidgeMode.Watershed;
            s.RidgeSpacing = ridgeSpacing * MmToFt;
            s.AddSplitLines = ChkSplit.IsChecked == true;
            ComboBoxItem ratio = CmbSaddleRatio.SelectedItem as ComboBoxItem;
            s.SaddleWidthRatio = ratio != null
                ? double.Parse((string)ratio.Tag, CultureInfo.InvariantCulture) : 1.0 / 3.0;
            s.SaddleSlopeFactor = saddleFactor;
            s.MaxDrainSpacing = maxSpacing * MmToFt;

            s.AddCurvePoints = ChkCurve.IsChecked == true;
            s.CurvePointCount = (int)Math.Round(curveCount);
            s.LargeCurveLength = curveBig * MmToFt;
            s.SmallCurveMidpoint = ChkCurveMid.IsChecked == true;
            s.MinGap = curveGap * MmToFt;
            s.GapMode = (GapMode)Math.Max(0, CmbGapMode.SelectedIndex);

            // remembered for next time; the drain ticks in the grid are what this run uses
            s.DrainMaxArea = drainMax * M2ToFt2;
            s.LowestSizeOnly = ChkLowest.IsChecked == true;

            s.Slope = slope;
            s.SlopeUnitIndex = Math.Max(0, CmbSlopeUnit.SelectedIndex);
            s.MinFallN = minFall;
            s.MaxHeightWarn = maxHeight * MmToFt;

            s.ResetShape = ChkReset.IsChecked == true;
            s.MergeTolerance = merge * MmToFt;
            s.RoundHeightTo = round * MmToFt;
            return true;
        }
    }
}
