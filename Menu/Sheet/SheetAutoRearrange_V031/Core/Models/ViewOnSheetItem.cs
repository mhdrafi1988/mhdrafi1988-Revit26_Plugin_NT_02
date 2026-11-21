using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V031.Core.Models
{
    /// <summary>Per-row fit status shown in the grid's Fit column and used to drive the preview.</summary>
    public enum ViewFitStatus
    {
        /// <summary>Not ticked — excluded from this run, no fit evaluated.</summary>
        NotSelected,

        /// <summary>Ticked and not yet packed (initial/idle state before Run).</summary>
        Pending,

        /// <summary>Packed successfully within the titleblock usable area.</summary>
        Fits,

        /// <summary>Ticked but did not fit — skipped under PlaceWhatsPlaceable.</summary>
        Overflow
    }

    /// <summary>
    /// One row in the Views on Sheet grid. Wraps a Revit Viewport/View pair plus
    /// the UI-editable IsChecked flag and the live FitStatus set after a preview
    /// or Run pass.
    ///
    /// V031 CHANGE: also holds the view title's measured geometry. <see cref="WidthMm"/> /
    /// <see cref="HeightMm"/> are now the packing footprint: the box alone (as in V030),
    /// or the box plus its title once <see cref="ApplyTitleLayout"/> runs with titles in the
    /// footprint. The packers are unchanged; they pack footprints, and
    /// <see cref="BoxCenterOffsetFt"/> turns a packed footprint centre back into a box centre.
    /// </summary>
    public partial class ViewOnSheetItem : ObservableObject
    {
        private const double MmToFeet = 1.0 / 304.8;

        /// <summary>The underlying Viewport element placed on the active sheet.</summary>
        public ElementId ViewportId { get; }

        /// <summary>The View shown through that viewport.</summary>
        public ElementId ViewId { get; }

        /// <summary>The view's name.</summary>
        public string ViewName { get; }

        /// <summary>Raw Revit ViewType — used by ViewTypeGroupResolver for gap-group and filter matching (strongly typed, not string-parsed).</summary>
        public ViewType ViewType { get; }

        /// <summary>Display string for the grid's Type column (ViewType.ToString()).</summary>
        public string ViewTypeName { get; }

        /// <summary>Formatted "1:N" scale string, or "—" for view types with no Scale (e.g. schedules).</summary>
        public string ScaleDisplay { get; }

        /// <summary>Packing footprint width on the sheet, in mm (the viewport box alone unless titles are in the footprint).</summary>
        public double WidthMm { get; private set; }

        /// <summary>Packing footprint height on the sheet, in mm (the viewport box alone unless titles are in the footprint).</summary>
        public double HeightMm { get; private set; }

        /// <summary>Current viewport box center on the sheet (feet, Revit internal units) — used for row-grouping in Sheet Order Rearrange.</summary>
        public XYZ CurrentCenter { get; private set; }

        // ── V031: measured geometry, feet, relative to the box's lower-left corner ──

        /// <summary>Viewport box width, feet.</summary>
        public double BoxWidthFt { get; private set; }

        /// <summary>Viewport box height, feet.</summary>
        public double BoxHeightFt { get; private set; }

        /// <summary>True when the viewport shows a title Revit can measure.</summary>
        public bool HasTitle { get; private set; }

        /// <summary>Title outline as measured, relative to the box's lower-left corner (feet).</summary>
        public double TitleMinX { get; private set; }

        /// <inheritdoc cref="TitleMinX"/>
        public double TitleMinY { get; private set; }

        /// <inheritdoc cref="TitleMinX"/>
        public double TitleMaxX { get; private set; }

        /// <inheritdoc cref="TitleMinX"/>
        public double TitleMaxY { get; private set; }

        /// <summary>Title extension line length as measured (feet); 0 when unknown.</summary>
        public double LineLengthFt { get; private set; }

        /// <summary>Title height (feet); 0 without a title.</summary>
        public double TitleHeightFt => HasTitle ? TitleMaxY - TitleMinY : 0;

        // ── V031: result of ApplyTitleLayout, feet, relative to the footprint centre ──

        /// <summary>Box centre minus footprint centre (feet). Zero when the footprint is the box.</summary>
        public XYZ BoxCenterOffsetFt { get; private set; } = XYZ.Zero;

        /// <summary>Predicted title rectangle after the title options, relative to the footprint centre (feet). Null without a title.</summary>
        public (double MinX, double MinY, double MaxX, double MaxY)? PredictedTitleRect { get; private set; }

        [ObservableProperty]
        private bool isChecked = true;

        [ObservableProperty]
        private ViewFitStatus fitStatus = ViewFitStatus.Pending;

        /// <summary>Creates a row from a viewport's box; call <see cref="SetTitleGeometry"/> to add its title.</summary>
        public ViewOnSheetItem(
            ElementId viewportId,
            ElementId viewId,
            string viewName,
            ViewType viewType,
            string viewTypeName,
            string scaleDisplay,
            double widthMm,
            double heightMm,
            XYZ currentCenter)
        {
            ViewportId = viewportId;
            ViewId = viewId;
            ViewName = viewName;
            ViewType = viewType;
            ViewTypeName = viewTypeName;
            ScaleDisplay = scaleDisplay;
            WidthMm = widthMm;
            HeightMm = heightMm;
            CurrentCenter = currentCenter;
            BoxWidthFt = widthMm * MmToFeet;
            BoxHeightFt = heightMm * MmToFeet;
        }

        /// <summary>
        /// V031: stores the box and title geometry read from the viewport (feet, sheet space).
        /// <paramref name="titleMin"/> / <paramref name="titleMax"/> null = no title shown.
        /// </summary>
        public void SetTitleGeometry(XYZ boxMin, XYZ boxMax, XYZ? titleMin, XYZ? titleMax, double lineLengthFt)
        {
            BoxWidthFt = boxMax.X - boxMin.X;
            BoxHeightFt = boxMax.Y - boxMin.Y;
            CurrentCenter = (boxMin + boxMax) * 0.5;
            HasTitle = titleMin != null && titleMax != null
                       && titleMax.X > titleMin.X && titleMax.Y > titleMin.Y;
            if (HasTitle)
            {
                TitleMinX = titleMin!.X - boxMin.X;
                TitleMinY = titleMin.Y - boxMin.Y;
                TitleMaxX = titleMax!.X - boxMin.X;
                TitleMaxY = titleMax.Y - boxMin.Y;
            }
            LineLengthFt = lineLengthFt > 0 ? lineLengthFt : 0;
        }

        /// <summary>
        /// V031: the title move that puts this title in the standard place — left edge on the
        /// box's left edge, top <paramref name="gapFt"/> below the box. Add it to
        /// <c>Viewport.LabelOffset</c>. Zero without a title.
        /// </summary>
        public XYZ StandardTitleShift(double gapFt) =>
            HasTitle ? new XYZ(-TitleMinX, -gapFt - TitleMaxY, 0) : XYZ.Zero;

        /// <summary>
        /// V031: recomputes <see cref="WidthMm"/>, <see cref="HeightMm"/>,
        /// <see cref="BoxCenterOffsetFt"/> and <see cref="PredictedTitleRect"/> for the given
        /// title options. <paramref name="bandHeightFt"/> is the tallest title among the views
        /// being packed (used when baselines are aligned). Pure arithmetic, no Revit calls.
        /// </summary>
        public void ApplyTitleLayout(ViewTitleSettings? s, double bandHeightFt)
        {
            double tMinX = TitleMinX, tMinY = TitleMinY, tMaxX = TitleMaxX, tMaxY = TitleMaxY;

            if (s != null && HasTitle)
            {
                if (s.LineMatchesWidth && LineLengthFt > 0)
                {
                    // The outline's right edge follows the line end, unless the text is longer
                    // than the line; then the text keeps it where it is.
                    double delta = BoxWidthFt - LineLengthFt;
                    bool textLonger = (tMaxX - tMinX) > LineLengthFt + 1.0 * MmToFeet;
                    tMaxX = textLonger ? Math.Max(tMaxX, tMaxX + delta) : Math.Max(tMinX + 1e-6, tMaxX + delta);
                }
                if (s.StandardOffset)
                {
                    XYZ shift = StandardTitleShift(s.GapBelowMm * MmToFeet);
                    tMinX += shift.X; tMaxX += shift.X;
                    tMinY += shift.Y; tMaxY += shift.Y;
                }
            }

            double minX = 0, minY = 0, maxX = BoxWidthFt, maxY = BoxHeightFt;
            if (s != null && s.IncludeInFootprint)
            {
                if (HasTitle)
                {
                    minX = Math.Min(minX, tMinX);
                    maxX = Math.Max(maxX, tMaxX);
                    minY = Math.Min(minY, tMinY);
                    maxY = Math.Max(maxY, tMaxY);
                }
                if (s.BaselinesActive)
                {
                    // Same band under every view, so bottom-aligned footprints give bottom-aligned
                    // boxes, and the titles' tops (and lines) land on one row line.
                    minY = Math.Min(minY, -(s.GapBelowMm * MmToFeet) - bandHeightFt);
                }
            }

            WidthMm = (maxX - minX) / MmToFeet;
            HeightMm = (maxY - minY) / MmToFeet;
            double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0;
            BoxCenterOffsetFt = new XYZ(BoxWidthFt / 2.0 - cx, BoxHeightFt / 2.0 - cy, 0);
            PredictedTitleRect = HasTitle ? (tMinX - cx, tMinY - cy, tMaxX - cx, tMaxY - cy) : null;
        }
    }
}
