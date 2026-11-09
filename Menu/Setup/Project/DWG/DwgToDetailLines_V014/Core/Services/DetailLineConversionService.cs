// ==============================================
// File: DetailLineConversionService.cs
// Layer: Core/Services
// Converts CAD import geometry into Detail Curves and Filled Regions in
// the active Drafting View, scoped to the layers the user checked in the
// Layers &amp; Hatches grid. Line layers -> DetailCurve. Hatch layers ->
// FilledRegion, using boundary loops from CadGeometryExtractor.ExtractHatches.
// Style/pattern resolution per layer (Create/Skip prompt, cached) mirrors
// the same pattern for both paths.
// Changes vs V010: log delegate switched from Action<string, Brush> to the
// shared Action<LogEntry> used across the rest of the suite.
// ==============================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Models;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Services
{
    public class DetailLineConversionService
    {
        private readonly UIApplication _uiApp;
        private readonly System.Action<LogEntry> _log;
        private readonly System.Action<double, string> _progress;
        private int _lastPercent = -1;

        /// <param name="uiApp">The running Revit application.</param>
        /// <param name="log">Receives activity log entries.</param>
        /// <param name="progress">Optional: receives (percent 0-100, status text) while converting.</param>
        public DetailLineConversionService(UIApplication uiApp, System.Action<LogEntry> log,
            System.Action<double, string> progress = null)
        {
            _uiApp = uiApp;
            _log = log;
            _progress = progress;
        }

        /// <summary>Reports progress, skipping updates that don't change the whole percent.</summary>
        private void Report(double percent, string text, bool force = false)
        {
            if (_progress == null)
                return;

            int whole = (int)System.Math.Clamp(percent, 0, 100);
            if (!force && whole == _lastPercent)
                return;

            _lastPercent = whole;
            _progress(whole, text);
        }

        public ConversionMetrics Execute(
            ImportInstance cad,
            SplineHandlingMode spline,
            TransformMethod transformMethod,
            int entityCount,
            int layerCount,
            IReadOnlyCollection<string> selectedLineLayers,
            IReadOnlyCollection<string> selectedHatchLayers,
            string defaultLineStyleName,
            string defaultFillPatternName,
            IReadOnlyDictionary<string, string> lineStyleMap = null,
            IReadOnlyDictionary<string, string> hatchTypeMap = null)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            View activeView = _uiApp.ActiveUIDocument.ActiveView;
            double tol = doc.Application.ShortCurveTolerance;

            var metrics = new ConversionMetrics
            {
                LayersFound = layerCount,
                Entities = entityCount
            };

            int placed = 0;
            int skipped = 0;
            int failed = 0;

            var lineSet = new HashSet<string>(selectedLineLayers ?? System.Array.Empty<string>());
            var hatchSet = new HashSet<string>(selectedHatchLayers ?? System.Array.Empty<string>());

            // The line pass owns the first part of the bar and the hatch pass the rest; a pass
            // that has nothing selected gives its share to the other one.
            double lineEnd = hatchSet.Count == 0 ? 100 : lineSet.Count == 0 ? 0 : 60;

            Report(0, "Starting...", force: true);

            var preprocessorResult = TransactionHelper.Run(doc, "DWG to Detail Lines", () =>
            {
                RunLinePass(cad, doc, activeView, spline, transformMethod, tol, lineSet,
                    defaultLineStyleName, lineStyleMap, 0, lineEnd, ref placed, ref skipped, ref failed);

                RunHatchPass(cad, doc, activeView, transformMethod, hatchSet,
                    defaultFillPatternName, hatchTypeMap, lineEnd, 100, ref placed, ref skipped, ref failed);

                Report(100, "Committing changes...", force: true);
            });

            // Elements that were created (counted in `placed` above) but then
            // rejected by Revit's own commit-time geometry validation (e.g.
            // "Line is too short.") were auto-deleted by
            // AutoDeleteFailuresPreprocessor rather than shown as a blocking
            // dialog. Move that count from Placed to Skipped so metrics
            // reflect what's actually in the model.
            if (preprocessorResult.DeletedElementCount > 0)
            {
                placed = System.Math.Max(0, placed - preprocessorResult.DeletedElementCount);
                skipped += preprocessorResult.DeletedElementCount;

                foreach (string msg in preprocessorResult.ResolvedMessages)
                {
                    ToolCancellation.CheckCurrent();
                    _log(new LogEntry(LogLevel.Warning, $"Element auto-removed at commit: {msg}"));
                }
            }

            metrics.Placed = placed;
            metrics.Skipped = skipped;
            metrics.Failed = failed;

            _log(new LogEntry(LogLevel.Success,
                $"Conversion complete | {placed} placed | {skipped} skipped | {failed} failed"));

            return metrics;
        }

        /// <summary>The style / type name chosen for a layer, or null when it has no explicit mapping.</summary>
        private static string MappedName(IReadOnlyDictionary<string, string> map, string layer)
            => map != null && map.TryGetValue(layer, out string name) ? name : null;

        private void RunLinePass(
            ImportInstance cad,
            Document doc,
            View activeView,
            SplineHandlingMode spline,
            TransformMethod transformMethod,
            double tol,
            HashSet<string> lineSet,
            string defaultLineStyleName,
            IReadOnlyDictionary<string, string> lineStyleMap,
            double pctFrom,
            double pctTo,
            ref int placed,
            ref int skipped,
            ref int failed)
        {
            if (lineSet.Count == 0)
                return;

            Report(pctFrom, "Reading CAD lines...", force: true);
            var curves = CadGeometryExtractor.Extract(cad, doc, activeView, spline, transformMethod, _log, out int extractionSkipped);
            skipped += extractionSkipped;
            var byLayer = curves.Where(c => lineSet.Contains(c.Layer)).GroupBy(c => c.Layer).ToList();
            int totalCurves = System.Math.Max(1, byLayer.Sum(g => g.Count()));
            int doneCurves = 0;

            var resolver = new LineStyleResolutionService();
            var styleService = new DetailLineStyleService(doc, resolver);

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

                skipped += shortCount;
                doneCurves += shortCount;

                if (shortCount > 0)
                {
                    _log(new LogEntry(LogLevel.Info, $"Layer '{layerGroup.Key}': short curves skipped = {shortCount}"));
                }

                GraphicsStyle style = styleService.GetOrResolve(
                    layerGroup.Key, MappedName(lineStyleMap, layerGroup.Key));

                if (style == null)
                {
                    _log(new LogEntry(LogLevel.Warning, $"Layer '{layerGroup.Key}' skipped by user choice"));
                    skipped += usable.Count;
                    doneCurves += usable.Count;
                    continue;
                }

                int layerPlaced = 0;
                int layerFailed = 0;

                foreach (var c in usable)
                {
                    ToolCancellation.CheckCurrent();
                    // Pre-filter above catches raw curve.Length < tol, but Revit's
                    // actual endpoint-distance check inside NewDetailCurve can still
                    // reject curves that pass that filter (e.g. arcs, post-transform
                    // geometry). Isolate per-curve so one bad curve doesn't abort the
                    // whole layer/transaction — log it as Failed and move on.
                    doneCurves++;
                    Report(pctFrom + (pctTo - pctFrom) * doneCurves / totalCurves,
                        $"Lines: layer {layerGroup.Key} ({doneCurves}/{totalCurves})");

                    try
                    {
                        DetailCurve detailCurve = doc.Create.NewDetailCurve(activeView, c);
                        detailCurve.LineStyle = style;
                        layerPlaced++;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                    {
                        layerFailed++;
                        _log(new LogEntry(LogLevel.Warning, $"Layer '{layerGroup.Key}': curve rejected by Revit ({ex.Message})"));
                    }
                }

                placed += layerPlaced;
                failed += layerFailed;

                _log(new LogEntry(LogLevel.Info,
                    $"Detail line style assigned: {layerGroup.Key} ({layerPlaced} lines" +
                    (layerFailed > 0 ? $", {layerFailed} failed)" : ")")));
            }
        }

        private void RunHatchPass(
            ImportInstance cad,
            Document doc,
            View activeView,
            TransformMethod transformMethod,
            HashSet<string> hatchSet,
            string defaultFillPatternName,
            IReadOnlyDictionary<string, string> hatchTypeMap,
            double pctFrom,
            double pctTo,
            ref int placed,
            ref int skipped,
            ref int failed)
        {
            if (hatchSet.Count == 0)
                return;

            Report(pctFrom, "Reading CAD hatches...", force: true);
            var hatches = CadGeometryExtractor.ExtractHatches(cad, doc, activeView, transformMethod, _log);
            var byLayer = hatches.Where(h => hatchSet.Contains(h.Layer)).GroupBy(h => h.Layer).ToList();
            int totalHatches = System.Math.Max(1, byLayer.Sum(g => g.Count()));
            int doneHatches = 0;

            var resolver = new FillPatternResolutionService();
            var styleService = new DetailFillRegionStyleService(doc, resolver, defaultFillPatternName);

            foreach (var layerGroup in byLayer)
            {
                ToolCancellation.CheckCurrent();
                FilledRegionType type = styleService.GetOrResolve(
                    layerGroup.Key, MappedName(hatchTypeMap, layerGroup.Key));

                if (type == null)
                {
                    int count = layerGroup.Count();
                    _log(new LogEntry(LogLevel.Warning, $"Hatch layer '{layerGroup.Key}' skipped by user choice"));
                    skipped += count;
                    doneHatches += count;
                    continue;
                }

                int layerPlaced = 0;
                int layerFailed = 0;

                foreach (var h in layerGroup)
                {
                    ToolCancellation.CheckCurrent();
                    // A degenerate or self-intersecting boundary loop (rare, but
                    // possible from faceted DWG solids) throws inside NewFilledRegion.
                    // Isolate per-hatch so one bad boundary doesn't abort the layer.
                    doneHatches++;
                    Report(pctFrom + (pctTo - pctFrom) * doneHatches / totalHatches,
                        $"Hatches: layer {layerGroup.Key} ({doneHatches}/{totalHatches})");

                    try
                    {
                        var loops = new List<CurveLoop> { h.Boundary };
                        FilledRegion.Create(doc, type.Id, activeView.Id, loops);
                        layerPlaced++;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                    {
                        layerFailed++;
                        _log(new LogEntry(LogLevel.Warning, $"Hatch layer '{layerGroup.Key}': boundary rejected by Revit ({ex.Message})"));
                    }
                }

                placed += layerPlaced;
                failed += layerFailed;

                _log(new LogEntry(LogLevel.Info,
                    $"Fill pattern assigned: {layerGroup.Key} ({layerPlaced} region(s)" +
                    (layerFailed > 0 ? $", {layerFailed} failed)" : ")")));
            }
        }
    }
}
