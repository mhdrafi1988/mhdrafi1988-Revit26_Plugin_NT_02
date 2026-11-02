// File: DrainItem.cs
// Location: Core/Models/
// Ported from AutoSlopeByDrain V004 (roof). Floor V001 adds fixture drains
// (Plumbing Fixture families on the floor, ShapeType "Fixture").
// CHANGE (V005): converted from hand-rolled INotifyPropertyChanged to
// CommunityToolkit.Mvvm's ObservableObject + [ObservableProperty], per
// Rafi's confirmed decision to use the real source-generator attributes
// for this tool. Behavior is identical — IsSelected still raises a
// change notification on every set so the grid checkbox and the
// ViewModel's selected-count / Run-enabled logic stay in sync.

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public partial class DrainItem : ObservableObject
    {
        public XYZ CenterPoint { get; set; } // Keep for backward compatibility
        public double Width { get; set; } // in mm
        public double Height { get; set; } // in mm
        public string SizeCategory => $"{Width:F0} x {Height:F0} mm";
        public string ShapeType { get; set; } = "Unknown";

        /// <summary>ShapeType of a drain that is a Plumbing Fixture family on the floor, not a sketch opening.</summary>
        public const string FixtureShapeType = "Fixture";

        /// <summary>True when this drain is a Plumbing Fixture family instance (no opening loop).</summary>
        public bool IsFixture => ShapeType == FixtureShapeType;

        /// <summary>Family and type name of a fixture drain, shown in the log; empty for openings.</summary>
        public string FixtureName { get; set; } = string.Empty;

        /// <summary>Grid text: the family and type name for a drain family, the size otherwise.</summary>
        public string SizeOrName => IsFixture && !string.IsNullOrEmpty(FixtureName) ? FixtureName : SizeCategory;

        /// <summary>Coarse bucket used to group the drain grid: "Circle", "Rectangle" (also covers Square), "Fixture" (drain family) or "Other" (Polygon/Unknown).</summary>
        public string ShapeGroup =>
            IsFixture ? "Fixture" :
            ShapeType == "Circle" ? "Circle" :
            (ShapeType == "Rectangle" || ShapeType == "Square") ? "Rectangle" :
            "Other";

        /// <summary>Sort key for "sorted by opening size" within a group — the loop's bounding-box area.</summary>
        public double SizeSortKey => Width * Height;

        /// <summary>Diameter in mm, for Circle rows only (bounding-box width == height); null for non-circle shapes so the grid cell renders blank.</summary>
        public double? Diameter => ShapeGroup == "Circle" ? Width : (double?)null;

        /// <summary>Radius in mm, for Circle rows only; null for non-circle shapes so the grid cell renders blank.</summary>
        public double? Radius => ShapeGroup == "Circle" ? Width / 2.0 : (double?)null;

        /// <summary>Fixed display order for ShapeGroup, so groups always list as Circle, Rectangle, Other regardless of detection order.</summary>
        /// <summary>NEW (V013). Plan position in metres for the grid (V012 showed internal feet with no unit).</summary>
        public double CenterXm => (CenterPoint?.X ?? 0) * 0.3048;

        /// <summary>NEW (V013). Plan position in metres for the grid.</summary>
        public double CenterYm => (CenterPoint?.Y ?? 0) * 0.3048;

        public int ShapeGroupOrder => ShapeGroup == "Circle" ? 0 : ShapeGroup == "Rectangle" ? 1 : ShapeGroup == "Other" ? 2 : 3;

        [ObservableProperty]
        private bool isSelected = true;

        public ElementId ElementId { get; set; }

        // Store the actual shape editing vertices that belong to this drain
        public List<SlabShapeVertex> DrainVertices { get; set; }

        // Store the loop curves for accurate distance checking
        public List<Curve> LoopCurves { get; set; }

        // Store corner points for accurate distance calculation
        public List<XYZ> CornerPoints { get; set; }

        // Track which drain this belongs to for pathfinding
        public int DrainId { get; set; }
        private static int _nextDrainId = 1;

        // Constructor with drain vertices
        public DrainItem(List<SlabShapeVertex> drainVertices, List<Curve> loopCurves,
                        double width, double height, string shapeType, ElementId id = null)
        {
            DrainVertices = drainVertices ?? new List<SlabShapeVertex>();
            LoopCurves = loopCurves ?? new List<Curve>();
            Width = width;
            Height = height;
            ShapeType = shapeType;
            ElementId = id;
            DrainId = _nextDrainId++;

            // Circular openings are selected by default; other shapes start unchecked
            // — confirmed UX decision so the grid doesn't default to sloping toward
            // every detected opening, only the ones almost always meant as drains.
            IsSelected = shapeType == "Circle";

            // Calculate center point as average of all drain vertices
            if (drainVertices != null && drainVertices.Count > 0)
            {
                CenterPoint = new XYZ(
                    drainVertices.Average(v => v.Position.X),
                    drainVertices.Average(v => v.Position.Y),
                    drainVertices.Average(v => v.Position.Z)
                );
            }
            else if (loopCurves != null && loopCurves.Count > 0)
            {
                // Fallback: compute the center directly from the loop's own curve
                // endpoints, which always exist regardless of nearby shape vertices.
                var loopPoints = new List<XYZ>();
                foreach (var c in loopCurves)
                {
                    loopPoints.Add(c.GetEndPoint(0));
                    loopPoints.Add(c.GetEndPoint(1));
                }
                CenterPoint = new XYZ(
                    loopPoints.Average(p => p.X),
                    loopPoints.Average(p => p.Y),
                    loopPoints.Average(p => p.Z)
                );
            }
            else
            {
                // Should not happen in practice (DrainDetectionService always supplies
                // loop curves), but guarantees CenterPoint is never null.
                CenterPoint = XYZ.Zero;
            }

            CornerPoints = CalculateCornerPoints();
        }

        // Legacy constructor for backward compatibility
        public DrainItem(XYZ center, double width, double height, ElementId id = null)
        {
            CenterPoint = center;
            Width = width;
            Height = height;
            ElementId = id;
            DrainId = _nextDrainId++;
            DrainVertices = new List<SlabShapeVertex>();
            LoopCurves = new List<Curve>();
            CornerPoints = CalculateCornerPoints();
        }

        public DrainItem(XYZ center, double width, double height, string shapeType, ElementId id = null)
        {
            CenterPoint = center;
            Width = width;
            Height = height;
            ShapeType = shapeType;
            ElementId = id;
            DrainId = _nextDrainId++;
            DrainVertices = new List<SlabShapeVertex>();
            LoopCurves = new List<Curve>();
            CornerPoints = CalculateCornerPoints();
        }

        private List<XYZ> CalculateCornerPoints()
        {
            var corners = new List<XYZ>();

            // Convert dimensions from mm to feet for Revit coordinates
            double halfWidth = (Width / 304.8) / 2;
            double halfHeight = (Height / 304.8) / 2;

            corners.Add(new XYZ(CenterPoint.X - halfWidth, CenterPoint.Y - halfHeight, CenterPoint.Z)); // Bottom-left
            corners.Add(new XYZ(CenterPoint.X + halfWidth, CenterPoint.Y - halfHeight, CenterPoint.Z)); // Bottom-right
            corners.Add(new XYZ(CenterPoint.X + halfWidth, CenterPoint.Y + halfHeight, CenterPoint.Z)); // Top-right
            corners.Add(new XYZ(CenterPoint.X - halfWidth, CenterPoint.Y + halfHeight, CenterPoint.Z)); // Top-left

            return corners;
        }

        public XYZ GetNearestCorner(XYZ point)
        {
            if (CornerPoints == null || CornerPoints.Count == 0)
                return CenterPoint; // Fallback to center if no corners

            XYZ nearestCorner = CornerPoints[0];
            double minDistance = point.DistanceTo(nearestCorner);

            for (int i = 1; i < CornerPoints.Count; i++)
            {
                double distance = point.DistanceTo(CornerPoints[i]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestCorner = CornerPoints[i];
                }
            }

            return nearestCorner;
        }

        // Check if a vertex belongs to this drain (within 5mm tolerance)
        public bool ContainsVertex(SlabShapeVertex vertex, double toleranceMm = 5.0)
        {
            if (vertex?.Position == null || LoopCurves == null) return false;

            double toleranceFeet = toleranceMm / 304.8;

            foreach (var curve in LoopCurves)
            {
                try
                {
                    double distance = curve.Distance(vertex.Position);
                    if (distance < toleranceFeet)
                        return true;
                }
                catch
                {
                    // Skip problematic curves
                }
            }

            return false;
        }
    }
}
