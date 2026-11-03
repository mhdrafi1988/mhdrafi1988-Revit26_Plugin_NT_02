using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorPointElevationSync.V001
{
    /// <summary>
    /// Finds shape-edit points on two floors that coincide in XY within a tolerance,
    /// and pairs them for elevation comparison.
    /// </summary>
    public class FloorPointMatchingService
    {
        private readonly Action<string, LogLevel> _log;

        public FloorPointMatchingService(Action<string, LogLevel> log)
        {
            _log = log;
        }

        /// <summary>
        /// tolerance is in feet (Revit internal units) — caller converts from mm before calling.
        /// </summary>
        public List<PointMatchRow> FindMatches(Floor floorA, Floor floorB, double xyToleranceFeet)
        {
            var result = new List<PointMatchRow>();

            SlabShapeEditor editorA = floorA.GetSlabShapeEditor();
            SlabShapeEditor editorB = floorB.GetSlabShapeEditor();

            if (editorA == null || editorB == null)
            {
                _log?.Invoke("One or both floors do not have a Slab Shape Editor enabled.", LogLevel.Error);
                return result;
            }

            List<SlabShapeVertex> vertsA = editorA.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();
            List<SlabShapeVertex> vertsB = editorB.SlabShapeVertices.Cast<SlabShapeVertex>().ToList();

            _log?.Invoke($"Floor A: {vertsA.Count} shape points. Floor B: {vertsB.Count} shape points.", LogLevel.Info);

            var usedB = new HashSet<int>();
            int pointCounter = 1;

            foreach (var va in vertsA)
            {
                double bestDist = double.MaxValue;
                int bestIndex = -1;

                for (int i = 0; i < vertsB.Count; i++)
                {
                    if (usedB.Contains(i)) continue;

                    var vb = vertsB[i];
                    double dx = va.Position.X - vb.Position.X;
                    double dy = va.Position.Y - vb.Position.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist <= xyToleranceFeet && dist < bestDist)
                    {
                        bestDist = dist;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                {
                    var vb = vertsB[bestIndex];
                    usedB.Add(bestIndex);

                    var row = new PointMatchRow
                    {
                        PointId = $"P-{pointCounter:000}",
                        X = UnitUtils.ConvertFromInternalUnits(va.Position.X, UnitTypeId.Millimeters),
                        Y = UnitUtils.ConvertFromInternalUnits(va.Position.Y, UnitTypeId.Millimeters),
                        ElevationA = UnitUtils.ConvertFromInternalUnits(va.Position.Z, UnitTypeId.Millimeters),
                        ElevationB = UnitUtils.ConvertFromInternalUnits(vb.Position.Z, UnitTypeId.Millimeters),
                        RevitPointA = va,
                        RevitPointB = vb,
                        IsIncluded = true
                    };

                    // Auto-exclude equal pairs per approved UI spec.
                    if (row.IsEqual)
                    {
                        row.IsIncluded = false;
                        _log?.Invoke($"{row.PointId}: elevations already equal — skipped.", LogLevel.Info);
                    }

                    result.Add(row);
                    pointCounter++;
                }
                else
                {
                    _log?.Invoke($"Floor A point at X={va.Position.X:F2}, Y={va.Position.Y:F2}: no match found within tolerance — skipped.", LogLevel.Warning);
                }
            }

            _log?.Invoke($"Found {result.Count} matching points within tolerance.", LogLevel.Info);
            return result;
        }

        /// <summary>
        /// Applies the higher elevation to the lower-elevation point of each included row.
        /// Must be called inside an active Transaction.
        /// </summary>
        public void ApplyMatches(SlabShapeEditor editorA, SlabShapeEditor editorB, IEnumerable<PointMatchRow> rows)
        {
            foreach (var row in rows.Where(r => r.IsIncluded && !r.IsEqual))
            {
                double targetElevationFeet = UnitUtils.ConvertToInternalUnits(row.HigherElevation, UnitTypeId.Millimeters);

                if (row.ElevationA < row.ElevationB)
                {
                    var vertexA = (SlabShapeVertex)row.RevitPointA;
                    editorA.ModifySubElement(vertexA, targetElevationFeet - vertexA.Position.Z);
                }
                else
                {
                    var vertexB = (SlabShapeVertex)row.RevitPointB;
                    editorB.ModifySubElement(vertexB, targetElevationFeet - vertexB.Position.Z);
                }
            }
        }
    }
}
