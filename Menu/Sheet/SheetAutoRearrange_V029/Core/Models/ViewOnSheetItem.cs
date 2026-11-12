using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
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
    /// </summary>
    public partial class ViewOnSheetItem : ObservableObject
    {
        /// <summary>The underlying Viewport element placed on the active sheet.</summary>
        public ElementId ViewportId { get; }

        /// <summary>The View shown through that viewport.</summary>
        public ElementId ViewId { get; }

        public string ViewName { get; }

        /// <summary>Raw Revit ViewType — used by ViewTypeGroupResolver for gap-group and filter matching (strongly typed, not string-parsed).</summary>
        public ViewType ViewType { get; }

        /// <summary>Display string for the grid's Type column (ViewType.ToString()).</summary>
        public string ViewTypeName { get; }

        /// <summary>Formatted "1:N" scale string, or "—" for view types with no Scale (e.g. schedules).</summary>
        public string ScaleDisplay { get; }

        /// <summary>Viewport bounding-box width on the sheet, in mm.</summary>
        public double WidthMm { get; }

        /// <summary>Viewport bounding-box height on the sheet, in mm.</summary>
        public double HeightMm { get; }

        /// <summary>Current viewport center point on the sheet (feet, Revit internal units) — used for row-grouping in Sheet Order Rearrange.</summary>
        public XYZ CurrentCenter { get; }

        [ObservableProperty]
        private bool isChecked = true;

        [ObservableProperty]
        private ViewFitStatus fitStatus = ViewFitStatus.Pending;

        // V014: SizeCategory (Tall/Wide/TallAndWide/Normal tag) REMOVED —
        // it existed purely to drive ShelfPackingService's anchor detection
        // and the grid's TALL/WIDE tag pill. Both are gone with the
        // Tall/Wide anchor system; see SheetAutoRearrangeSettings remarks.

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
        }
    }
}
