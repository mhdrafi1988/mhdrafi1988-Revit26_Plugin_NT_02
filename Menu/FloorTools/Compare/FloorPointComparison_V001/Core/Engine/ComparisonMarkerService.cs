using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Engine
{
    /// <summary>
    /// Places DetailCurve circles (two half-arcs forming a closed circle, planar
    /// to the active view's working elevation) marking Missing and Mismatched
    /// comparison points. Must be called from inside an already-open Transaction —
    /// this service does not open/commit its own transaction.
    /// </summary>
    public static class ComparisonMarkerService
    {
        public static (int missingPlaced, int mismatchPlaced) PlaceMarkers(
            Document doc,
            View activeView,
            List<PointComparisonEntry> entries,
            MarkerStyleGroup missingGroup,
            MarkerStyleGroup mismatchGroup,
            Action<LogEntry> log)
        {
            if (!(activeView is ViewPlan))
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    "Comparison Markers: active view is not a plan view — skipping marker placement."));
                return (0, 0);
            }

            double viewElevFt = (activeView as ViewPlan)?.GenLevel?.Elevation ?? 0;

            var missingPoints = entries
                .Where(e => e.Status == PointComparisonStatus.MissingInA || e.Status == PointComparisonStatus.MissingInB)
                .Select(e => e.Position)
                .ToList();

            var mismatchPoints = entries
                .Where(e => e.Status == PointComparisonStatus.MismatchedElevation)
                .Select(e => e.Position)
                .ToList();

            int missingPlaced = 0;
            foreach (XYZ pt in missingPoints)
            {
                ToolCancellation.CheckCurrent();
                if (PlaceOneCircle(doc, activeView, pt, viewElevFt, missingGroup, "Missing Point", log))
                    missingPlaced++;
            }
            log?.Invoke(new LogEntry(LogLevel.Success, $"Comparison Markers: placed {missingPlaced} missing-point circle(s)."));

            int mismatchPlaced = 0;
            foreach (XYZ pt in mismatchPoints)
            {
                ToolCancellation.CheckCurrent();
                if (PlaceOneCircle(doc, activeView, pt, viewElevFt, mismatchGroup, "Mismatched Elevation", log))
                    mismatchPlaced++;
            }
            log?.Invoke(new LogEntry(LogLevel.Success, $"Comparison Markers: placed {mismatchPlaced} mismatched-elevation circle(s)."));

            return (missingPlaced, mismatchPlaced);
        }

        private static bool PlaceOneCircle(
            Document doc,
            View activeView,
            XYZ centerPt,
            double viewElevFt,
            MarkerStyleGroup style,
            string label,
            Action<LogEntry> log)
        {
            try
            {
                double radiusFt = UnitUtils.ConvertToInternalUnits(style.RadiusMm, UnitTypeId.Millimeters);
                if (radiusFt <= 0)
                {
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Comparison Markers: skipped a {label} circle — radius must be greater than 0."));
                    return false;
                }

                XYZ center = new XYZ(centerPt.X, centerPt.Y, viewElevFt);

                Arc halfA = Arc.Create(center, radiusFt, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
                Arc halfB = Arc.Create(center, radiusFt, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);

                DetailCurve dcA = doc.Create.NewDetailCurve(activeView, halfA);
                DetailCurve dcB = doc.Create.NewDetailCurve(activeView, halfB);

                ApplyColor(activeView, dcA, style, log);
                ApplyColor(activeView, dcB, style, log);

                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"Comparison Markers: failed to place a {label} circle — {ex.Message}"));
                return false;
            }
        }

        private static void ApplyColor(View activeView, DetailCurve dc, MarkerStyleGroup style, Action<LogEntry> log)
        {
            if (dc == null) return;

            Color revitColor = NamedColorHelper.ToRevitColor(style.ColorName);
            try
            {
                OverrideGraphicSettings ogs = activeView.GetElementOverrides(dc.Id) ?? new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(revitColor);
                activeView.SetElementOverrides(dc.Id, ogs);
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"Comparison Markers: could not apply color '{style.ColorName}' — {ex.Message}"));
            }
        }
    }
}
