using System.Collections.Generic;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models
{
    public enum OpeningShapeType
    {
        Rectangle,
        Circle,
        Other
    }

    /// <summary>
    /// Represents one inner loop (void/opening) of the floor slab.
    /// Holds both geometric data and UI selection state.
    /// </summary>
    /// <remarks>
    /// Inherits <see cref="ObservableObject"/> so that <see cref="IsSelected"/> raises
    /// <c>PropertyChanged</c> notifications. <see cref="FloorRidgeViewModel"/> relies on this
    /// to detect manual checkbox/row selection in the Drainage Seeds grid and keep
    /// <c>SelectedOpeningsCount</c> / the Generate Ridges button in sync. Without this,
    /// only the Select All/None/Inverse commands (which call UpdateSelectedCount() directly)
    /// would update the UI — manual clicks would silently do nothing.
    /// </remarks>
    public partial class OpeningData : ObservableObject
    {
        /// <summary>Index in the original list of inner loops (for reference).</summary>
        public int Index { get; set; }

        /// <summary>Flattened 2D vertices of the loop (Z=0).</summary>
        public List<XYZ> Vertices { get; set; } = new List<XYZ>();

        /// <summary>Detected shape type: Rectangle, Circle, or Other.</summary>
        public OpeningShapeType ShapeType { get; set; }

        /// <summary>
        /// For Rectangle: Width (X extent). For Circle: Radius. For Other: Width of bounding box.
        /// </summary>
        public double Dim1 { get; set; }

        /// <summary>
        /// For Rectangle: Height (Y extent). For Circle: 0 (not used). For Other: Height of bounding box.
        /// </summary>
        public double Dim2 { get; set; }

        /// <summary>Bounding-box width (full X extent).</summary>
        public double BBoxWidth { get; set; }

        /// <summary>Bounding-box height (full Y extent).</summary>
        public double BBoxHeight { get; set; }

        /// <summary>Centroid = centre of the bounding box (per your requirement).</summary>
        public XYZ Centroid { get; set; }

        /// <summary>True if the user selects this loop as a drainage seed.</summary>
        [ObservableProperty]
        private bool _isSelected;

        // ── Display helpers (mm) ──────────────────────────────────────────────
        // Revit stores length in feet internally; the UI shows millimetres.
        private const double FeetToMm = 304.8;

        /// <summary>
        /// Human-readable dimension string in millimetres for the grouped seed list.
        /// Circle → "R 420"; Rectangle/Other → "1200 × 800" (BBox extents).
        /// </summary>
        public string DisplayDimensions => ShapeType == OpeningShapeType.Circle
            ? $"R {Dim1 * FeetToMm:F0}"
            : $"{BBoxWidth * FeetToMm:F0} × {BBoxHeight * FeetToMm:F0}";

        /// <summary>Bounding-box width in millimetres, formatted for display.</summary>
        public string DisplayBBoxWidth => $"{BBoxWidth * FeetToMm:F0}";
    }
}
