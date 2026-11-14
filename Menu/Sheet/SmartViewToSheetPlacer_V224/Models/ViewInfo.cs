using System.Collections.Generic;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Models
{
    /// <summary>
    /// Represents a single Revit view available for placement, along with its
    /// pre-computed size on sheet (mm) at its current scale. WidthMm/HeightMm
    /// are calculated once when views are loaded (Stage 1) and consumed
    /// directly by the packing algorithm in Stage 2 — never recomputed
    /// mid-packing.
    ///
    /// V213: added RightDirection/UpDirection/CropCenterModel/IsMarkerResolved —
    /// these feed the new reading-order sort (ReadingOrderPackingService),
    /// ported conceptually from APUS's section-marker projection but built
    /// from each View's own crop box center instead of a marker point.
    /// Populated once in the handler's ExecuteLoadViews, same pass as
    /// WidthMm/HeightMm — never recomputed mid-packing, same rule as above.
    /// </summary>
    public partial class ViewInfo : ObservableObject
    {
        /// <summary>The underlying Revit View ElementId.</summary>
        public ElementId ViewId { get; }

        /// <summary>View name as shown in the Project Browser.</summary>
        public string Name { get; }

        /// <summary>Raw Revit ViewType enum value (FloorPlan, Section, Elevation, Drafting, Detail, etc.).</summary>
        public ViewType RevitViewType { get; }

        /// <summary>Human-friendly label for RevitViewType (e.g. "Floor Plan", "Structural Plan").</summary>
        public string ViewTypeLabel { get; }

        /// <summary>View scale as an integer denominator (e.g. 100 for 1:100). Zero for scale-less views (3D, Legend).</summary>
        public int Scale { get; }

        /// <summary>Display string for scale, e.g. "1:100" or "—" if not applicable.</summary>
        public string ScaleLabel => Scale > 0 ? $"1:{Scale}" : "—";

        /// <summary>Width on sheet in millimeters (the view's paper-space outline; crop box ÷ scale as a fallback).</summary>
        public double WidthMm { get; }

        /// <summary>
        /// Height of the view itself on sheet in millimeters, without the title space
        /// (the view's paper-space outline; crop box ÷ scale as a fallback).
        /// </summary>
        public double ViewHeightMm { get; }

        /// <summary>
        /// Extra height in millimeters reserved below the view for its viewport title.
        /// 0 (the default) reserves nothing. Set by the view model from the Title space field.
        /// </summary>
        public double TitleSpaceMm
        {
            get => _titleSpaceMm;
            set
            {
                double clamped = value > 0 ? value : 0;
                if (clamped == _titleSpaceMm) return;
                _titleSpaceMm = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeightMm));
            }
        }
        private double _titleSpaceMm;

        /// <summary>
        /// Height the view takes up on the sheet in millimeters: <see cref="ViewHeightMm"/> plus
        /// <see cref="TitleSpaceMm"/>. This is the height the packing uses.
        /// </summary>
        public double HeightMm => ViewHeightMm + TitleSpaceMm;

        /// <summary>View's Right direction vector (model space), used to project the
        /// crop box center onto the U axis for reading-order sort. Null if the
        /// view type does not expose a meaningful Right direction (falls back
        /// to IsMarkerResolved = false).</summary>
        public XYZ? RightDirection { get; }

        /// <summary>View's Up direction vector (model space), used to project the
        /// crop box center onto the V axis (row-banding) for reading-order sort.</summary>
        public XYZ? UpDirection { get; }

        /// <summary>Crop box center in model space (X,Y), used as the reading-order
        /// sort anchor in place of APUS's section marker point. Null if the view
        /// has no crop box or CropBox read failed — see IsMarkerResolved.</summary>
        public XYZ? CropCenterModel { get; }

        /// <summary>Whether CropCenterModel/RightDirection/UpDirection were
        /// successfully resolved for this view. False views are logged as a
        /// Warning at load time and pushed to the end of the reading-order sort
        /// (SortFallback.PushToEnd) — they are still placed, never skipped or
        /// failed, only sorted last.</summary>
        public bool IsMarkerResolved { get; }

        /// <summary>Whether this view is checked for inclusion in the placement run.</summary>
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// V213: true once the user has manually toggled THIS row's checkbox
        /// by hand (via the grid), as opposed to IsSelected being set by the
        /// View Type popover's bulk check/uncheck. Confirmed with Rafi:
        /// once a row is manually touched, future popover toggles for that
        /// row's ViewType leave it alone — this flag is how
        /// ApplyViewTypeSelectionFromPopover() (Stage1 partial) knows which
        /// rows to skip. Never reset automatically — only a fresh LoadViews
        /// (new ViewInfo instances entirely) clears it.
        /// </summary>
        public bool WasManuallySetByUser { get; set; }

        /// <summary>
        /// V213: true if this view already has a real Revit sheet placement
        /// (an existing Viewport on some ViewSheet in the model), detected via
        /// View.GetPlacementOnSheetStatus() when views are loaded in the
        /// handler's ExecuteLoadViews. Independent of this tool session's own
        /// packing — reflects the model's actual current state. Drives Stage
        /// 1's All/Placed/Not-Placed filter toggle.
        /// </summary>
        public bool IsAlreadyPlaced { get; }

        /// <summary>
        /// False when Revit will refuse to put this view on another sheet: it is already
        /// on one and is not a legend (a legend may sit on any number of sheets).
        /// Such views are left out of the packing.
        /// </summary>
        public bool CanBePlaced => !IsAlreadyPlaced || RevitViewType == ViewType.Legend;

        /// <summary>Text for the grid's "On Sheet" column.</summary>
        public string OnSheetLabel =>
            !IsAlreadyPlaced ? "—" : (CanBePlaced ? "Yes – reusable" : "Yes – skipped");

        /// <summary>
        /// Every parameter this View element carries — built-in (exposed as an
        /// instance Parameter), project, and shared parameters bound to the
        /// Views category alike — keyed by Definition.Name, value as its
        /// display string (AsValueString/AsString). Populated once in the
        /// handler's ExecuteLoadViews via View.Parameters. Feeds Stage 1's
        /// generic Parameter/Value filter (FilterParameterOptions is the union
        /// of these keys across all loaded views).
        /// </summary>
        public IReadOnlyDictionary<string, string> ParameterValues { get; }

        public ViewInfo(
            ElementId viewId,
            string name,
            ViewType revitViewType,
            string viewTypeLabel,
            int scale,
            double widthMm,
            double heightMm,
            XYZ? rightDirection = null,
            XYZ? upDirection = null,
            XYZ? cropCenterModel = null,
            bool isMarkerResolved = false,
            bool isAlreadyPlaced = false,
            bool isSelected = false,
            IReadOnlyDictionary<string, string>? parameterValues = null)
        {
            ViewId = viewId;
            Name = name;
            RevitViewType = revitViewType;
            ViewTypeLabel = viewTypeLabel;
            Scale = scale;
            WidthMm = widthMm;
            ViewHeightMm = heightMm;
            RightDirection = rightDirection;
            UpDirection = upDirection;
            CropCenterModel = cropCenterModel;
            IsMarkerResolved = isMarkerResolved;
            IsAlreadyPlaced = isAlreadyPlaced;
            _isSelected = isSelected;
            ParameterValues = parameterValues ?? new Dictionary<string, string>();
        }
    }
}
