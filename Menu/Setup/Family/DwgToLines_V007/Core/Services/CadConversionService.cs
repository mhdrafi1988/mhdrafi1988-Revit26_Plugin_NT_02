// ==============================================
// File: CadConversionService.cs
// Layer: Core/Services
// Changes vs V004:
//   FIX  A new SketchPlane element was created once PER CURVE inside the
//        placement loop — now created once per Execute() call and reused.
//   FIX  SymbolicLineStyleService / LineStyleResolutionService existed but
//        were never called; symbolic curves got no line style at all. Now
//        wired in: each layer resolves (or creates, or is user-skipped) a
//        line style once, and every symbolic curve placed on that layer is
//        assigned it via CurveElement.LineStyle.
//   ADDED per-run summary log line ("X placed | Y skipped | Z failed") and
//        per-curve try/catch so one bad curve can't abort the whole layer.
// ==============================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.DwgToLines.V007.Core.Models;
using Revit26_Plugin_Revised.DwgToLines.V007.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using DBTransform = Autodesk.Revit.DB.Transform;

namespace Revit26_Plugin_Revised.DwgToLines.V007.Core.Services
{
    public class CadConversionService
    {
        private readonly UIApplication _uiApp;
        private readonly Action<LogEntry> _log;

        public CadConversionService(UIApplication uiApp, Action<LogEntry> log)
        {
            _uiApp = uiApp;
            _log = log;
        }

        public void Execute(
            ImportInstance cad,
            PlacementMode placement,
            SplineHandlingMode spline,
            bool extrusionReady)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            double tol = doc.Application.ShortCurveTolerance;

            var curves = CadGeometryExtractor.Extract(
                cad, doc, doc.ActiveView, spline, _log);

            var byLayer = curves.GroupBy(c => c.Layer);

            var resolver = new LineStyleResolutionService();
            var styleService = new SymbolicLineStyleService(doc, resolver);

            int placedCount = 0, skippedCount = 0, failedCount = 0;

            TransactionHelper.Run(doc, "DWG Convert", () =>
            {
                SketchPlane sketchPlane = SketchPlane.Create(
                    doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));

                foreach (var layerGroup in byLayer)
                {
                    ToolCancellation.CheckCurrent();
                    int shortCount = 0;
                    var usable = new List<Curve>();

                    foreach (var c in layerGroup)
                    {
                        ToolCancellation.CheckCurrent();
                        if (c.Curve.Length < tol)
                        {
                            shortCount++;
                            continue;
                        }
                        usable.Add(c.Curve);
                    }

                    if (extrusionReady)
                    {
                        usable = CurveCleanupHelper.SnapEndpoints(usable, tol);

                        if (!CurveCleanupHelper.FormsClosedLoop(usable, tol))
                            _log(new LogEntry(LogLevel.Warning, $"Layer '{layerGroup.Key}' not closed"));
                    }

                    if (shortCount > 0)
                        _log(new LogEntry(LogLevel.Info, $"Layer '{layerGroup.Key}': short curves skipped = {shortCount}"));

                    GraphicsStyle lineStyle = null;
                    if (placement != PlacementMode.ModelOnly)
                    {
                        lineStyle = styleService.GetOrResolve(layerGroup.Key);
                        if (lineStyle == null)
                        {
                            _log(new LogEntry(LogLevel.Warning, $"Layer '{layerGroup.Key}' skipped (no line style)"));
                            skippedCount += usable.Count;
                            continue;
                        }
                    }

                    foreach (var c in usable)
                    {
                        ToolCancellation.CheckCurrent();
                        try
                        {
                            if (placement != PlacementMode.ModelOnly)
                            {
                                SymbolicCurve symbolic = doc.FamilyCreate.NewSymbolicCurve(c, sketchPlane);
                                symbolic.LineStyle = lineStyle;
                            }

                            if (placement != PlacementMode.SymbolicOnly)
                            {
                                Curve flat = c.CreateTransformed(DBTransform.Identity);
                                doc.FamilyCreate.NewModelCurve(flat, sketchPlane);
                            }

                            placedCount++;
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            _log(new LogEntry(LogLevel.Error,
                                $"Failed to place curve on layer '{layerGroup.Key}': {ex.Message}"));
                        }
                    }
                }
            });

            _log(new LogEntry(LogLevel.Success,
                $"Conversion complete — {placedCount} placed | {skippedCount} skipped | {failedCount} failed"));
        }
    }
}
