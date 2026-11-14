// =======================================================
// File: EdgePointModel.cs
// Location: Core/Models/
// One edge (any type — Line, Arc, Ellipse, spline) of the picked floor's
// top face, one grid row. PreviewPointCount is refreshed by the
// ViewModel whenever the global settings change, and shows exactly how
// many points Apply will place on this edge.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorMultiplePoints.V001.Core.Models
{
    public partial class EdgePointModel : ObservableObject
    {
        public int    Index         { get; set; }
        public string CurveTypeName { get; set; }
        public double LengthM       { get; set; }
        public Curve  Geometry      { get; set; }

        /// <summary>Slope (rise/run × 100) of the single top facet this edge borders — used by the optional max-slope filter.</summary>
        public double FacetSlopePercent { get; set; }

        [ObservableProperty]
        private bool isSelected = true;

        [ObservableProperty]
        private int previewPointCount;
    }
}
