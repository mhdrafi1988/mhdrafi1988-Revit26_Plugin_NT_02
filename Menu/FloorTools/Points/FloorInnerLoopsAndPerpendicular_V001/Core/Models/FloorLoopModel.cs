// =======================================================
// File: FloorLoopModel.cs
// Location: Core/Models/
// Changes vs V003: converted from hand-rolled INotifyPropertyChanged to
// CommunityToolkit.Mvvm's ObservableObject + [ObservableProperty], per
// this suite's stack convention.
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models
{
    /// <summary>
    /// Represents a single boundary loop extracted from a floor, including its
    /// shape classification and perimeter. Used purely as the selectable shape
    /// source for the Perpendicular Border Point tool.
    /// </summary>
    public partial class FloorLoopModel : ObservableObject
    {
        /// <summary>Sequential index of the loop as discovered on the floor.</summary>
        public int Index { get; set; }

        /// <summary>Perimeter of the loop in millimetres.</summary>
        public double PerimeterMm { get; set; }

        /// <summary>Boundary role of the loop: <c>Outer</c> or <c>Inner</c>.</summary>
        public string LoopType { get; set; }

        /// <summary>Raw shape classification: Circular / Rectangle / Other.</summary>
        public string LoopShapeType { get; set; }

        /// <summary>User-facing group for hierarchical UI grouping.</summary>
        public string ShapeCategory => LoopShapeType switch
        {
            "Circular"  => "Circular",
            "Rectangle" => "Rectangular",
            _           => "Other"
        };

        /// <summary>Sort rank: Circular (0), Rectangular (1), Other (2).</summary>
        public int CategoryRank => LoopShapeType switch
        {
            "Circular"  => 0,
            "Rectangle" => 1,
            _           => 2
        };

        /// <summary>Whether this loop is included when perpendicular points are generated.</summary>
        [ObservableProperty]
        private bool isSelected = true;

        /// <summary>Underlying Revit curve loop geometry for the boundary.</summary>
        public CurveLoop Geometry { get; set; }
    }
}
