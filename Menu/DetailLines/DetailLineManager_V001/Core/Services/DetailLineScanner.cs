using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services
{
    /// <summary>
    /// Collects detail lines for a scope and builds the grid rows, duplicate clusters and line-style
    /// usage. Only detail lines of the host model are read (<c>CurveElementType.DetailCurve</c>:
    /// lines, arcs, splines, ellipses); linked models and sketch boundaries are left out.
    /// Call only in the Revit API context.
    /// </summary>
    public static class DetailLineScanner
    {
        /// <summary>Runs one search.</summary>
        /// <param name="uiDoc">Active document.</param>
        /// <param name="scope">Which lines to collect.</param>
        /// <param name="toleranceMm">Short-line and duplicate tolerance in mm.</param>
        /// <param name="log">Receives progress, skips and warnings.</param>
        public static ScanResult Scan(UIDocument uiDoc, SearchScope scope, double toleranceMm, Action<LogLevel, string> log)
        {
            var doc = uiDoc.Document;
            var result = new ScanResult();

            var sketchMembers = CollectSketchMembers(doc);
            var curves = Collect(uiDoc, scope, sketchMembers, log, out string scopeText, out int sketchSkipped);
            result.ScopeText = scopeText;

            int links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount();
            log(LogLevel.Info, $"Scope: {scopeText} · {curves.Count} detail line(s) collected"
                + (links > 0 ? $" · {links} linked model instance(s) not searched" : string.Empty)
                + (sketchSkipped > 0 ? $" · {sketchSkipped} sketch boundary line(s) left out" : string.Empty) + ".");

            var editability = new EditabilityService(doc);
            var styleCache = new Dictionary<ElementId, (string Name, int Weight)>();
            var viewCache = new Dictionary<ElementId, View>();
            var candidates = new List<DuplicateDetector.Candidate>(curves.Count);
            int unreadable = 0;

            foreach (var curve in curves)
            {
                try
                {
                    var row = BuildRow(doc, curve, editability, styleCache, viewCache, out Curve geometry);
                    candidates.Add(DuplicateDetector.Describe(result.Rows.Count, row.ViewId, row.Curve, geometry));
                    result.Rows.Add(row);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    unreadable++;
                    if (unreadable <= 20)
                        log(LogLevel.Warning, $"Skipped line {curve.Id.Value}: could not read it ({ex.Message}).");
                }
            }
            if (unreadable > 20)
                log(LogLevel.Warning, $"… {unreadable - 20} more unreadable line(s) skipped.");

            double tolFeet = UnitUtils.ConvertToInternalUnits(Math.Max(toleranceMm, 0.001), UnitTypeId.Millimeters);
            var clusters = DuplicateDetector.FindClusters(candidates, tolFeet);
            foreach (var pair in clusters)
                result.Rows[pair.Key].DuplicateCluster = pair.Value;
            result.DuplicateClusters = clusters.Values.Distinct().Count();

            foreach (var row in result.Rows)
                row.ApplyTolerance(toleranceMm);

            var usage = LineStyleService.CollectUsage(doc);
            result.Styles.AddRange(LineStyleService.LoadStyles(doc, usage));

            log(LogLevel.Success,
                $"Found {result.Rows.Count} line(s): {clusters.Count} in {result.DuplicateClusters} duplicate cluster(s), "
                + $"{result.Rows.Count(r => r.IsShort)} shorter than {toleranceMm:0.##} mm, "
                + $"{result.Rows.Count(r => r.InGroup)} in groups, {result.Rows.Count(r => r.IsOwnedByOther)} owned by other users. "
                + $"Line styles: {result.Styles.Count} ({result.Styles.Count(s => s.IsUnused)} unused, built-in styles excluded).");
            return result;
        }

        /// <summary>Maps a Revit curve to its <see cref="CurveKind"/>.</summary>
        public static CurveKind KindOf(Curve curve) => curve switch
        {
            Line => CurveKind.Line,
            Arc => CurveKind.Arc,
            NurbSpline or HermiteSpline => CurveKind.Spline,
            Ellipse => CurveKind.Ellipse,
            _ => CurveKind.Other
        };

        /// <summary>True for a detail line of this document (not a model, symbolic or separation line).</summary>
        public static bool IsDetailLine(Element element)
            => element is CurveElement c && c.CurveElementType == CurveElementType.DetailCurve;

        private static List<CurveElement> Collect(UIDocument uiDoc, SearchScope scope, HashSet<ElementId> sketchMembers,
                                                  Action<LogLevel, string> log, out string scopeText, out int sketchSkipped)
        {
            var doc = uiDoc.Document;
            IEnumerable<Element> source;

            switch (scope)
            {
                case SearchScope.Selected:
                {
                    var ids = uiDoc.Selection.GetElementIds();
                    scopeText = $"Selected ({ids.Count} element(s))";
                    if (ids.Count == 0)
                        log(LogLevel.Warning, "Nothing is selected in Revit. Select detail lines, then click Find lines.");
                    source = ids.Select(doc.GetElement).Where(e => e != null);
                    break;
                }
                case SearchScope.ViewsOnSheets:
                {
                    var viewIds = new HashSet<ElementId>();
                    var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
                    foreach (var sheet in sheets)
                    {
                        viewIds.Add(sheet.Id);
                        foreach (var id in sheet.GetAllPlacedViews()) viewIds.Add(id);
                    }
                    scopeText = $"Views on sheets ({viewIds.Count - sheets.Count} view(s) on {sheets.Count} sheet(s))";
                    source = new FilteredElementCollector(doc).OfClass(typeof(CurveElement))
                        .Where(e => e.OwnerViewId != null && viewIds.Contains(e.OwnerViewId));
                    break;
                }
                case SearchScope.WholeProject:
                    scopeText = "Whole project";
                    source = new FilteredElementCollector(doc).OfClass(typeof(CurveElement));
                    break;
                default:
                {
                    var view = uiDoc.ActiveView;
                    if (view == null)
                    {
                        scopeText = "Active view (none)";
                        log(LogLevel.Warning, "There is no active view.");
                        source = Enumerable.Empty<Element>();
                        break;
                    }
                    scopeText = $"Active view ({view.Name})";
                    // A view collector also returns detail lines a dependent view shows from its parent.
                    source = Enumerable.Empty<Element>();
                    if (view.IsTemplate)
                    {
                        log(LogLevel.Warning, "The active view is a view template; open a view and search again.");
                        break;
                    }
                    try
                    {
                        source = new FilteredElementCollector(doc, view.Id).OfClass(typeof(CurveElement)).ToElements();
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        log(LogLevel.Warning, $"Detail lines cannot be searched in '{view.Name}' ({view.ViewType}). Open a plan, section, drafting view or sheet.");
                    }
                    break;
                }
            }

            var list = new List<CurveElement>();
            sketchSkipped = 0;
            foreach (var e in source)
            {
                if (!IsDetailLine(e)) continue;
                if (sketchMembers.Contains(e.Id)) { sketchSkipped++; continue; }
                list.Add((CurveElement)e);
            }
            return list;
        }

        // Boundary lines of filled regions, masking regions and other sketches are detail curves too,
        // but deleting or restyling them would break their sketch.
        private static HashSet<ElementId> CollectSketchMembers(Document doc)
        {
            var set = new HashSet<ElementId>();
            foreach (Sketch sketch in new FilteredElementCollector(doc).OfClass(typeof(Sketch)))
            {
                try { foreach (var id in sketch.GetAllElements()) set.Add(id); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* unreadable sketch: nothing to exclude */ }
            }

            var curveFilter = new ElementClassFilter(typeof(CurveElement));
            foreach (var region in new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)))
            {
                try { foreach (var id in region.GetDependentElements(curveFilter)) set.Add(id); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { /* nothing to exclude */ }
            }
            return set;
        }

        private static DetailLineRow BuildRow(Document doc, CurveElement curve, EditabilityService editability,
                                              Dictionary<ElementId, (string Name, int Weight)> styleCache,
                                              Dictionary<ElementId, View> viewCache, out Curve geometry)
        {
            geometry = curve.GeometryCurve;

            string styleName = string.Empty;
            int styleWeight = 1;
            ElementId styleCatId = null;
            if (curve.LineStyle is GraphicsStyle gs && gs.GraphicsStyleCategory is Category cat)
            {
                styleCatId = cat.Id;
                if (!styleCache.TryGetValue(cat.Id, out var style))
                    styleCache[cat.Id] = style = (cat.Name, LineStyleService.GetWeight(cat));
                (styleName, styleWeight) = style;
            }

            var viewId = curve.OwnerViewId ?? ElementId.InvalidElementId;
            if (!viewCache.TryGetValue(viewId, out var view))
                viewCache[viewId] = view = doc.GetElement(viewId) as View;

            int overrideWeight = 0;
            if (view != null)
            {
                try
                {
                    int pen = view.GetElementOverrides(curve.Id).ProjectionLineWeight;
                    if (pen != OverrideGraphicSettings.InvalidPenNumber && pen > 0) overrideWeight = pen;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // View without element overrides (legends, schedules): no override to show.
                }
            }

            return new DetailLineRow
            {
                Id = curve.Id,
                StyleName = styleName,
                StyleCategoryId = styleCatId,
                StyleWeight = styleWeight,
                OverrideWeight = overrideWeight,
                ViewId = viewId,
                ViewName = view?.Name ?? string.Empty,
                ViewTypeName = view == null ? string.Empty : ViewTypeLabel(view.ViewType),
                LengthMm = geometry == null ? 0 : UnitUtils.ConvertFromInternalUnits(geometry.Length, UnitTypeId.Millimeters),
                Curve = KindOf(geometry),
                WorksetName = editability.WorksetName(curve),
                Comment = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty,
                InGroup = EditabilityService.InGroup(curve),
                OwnedBy = editability.OwnedByOther(curve)
            };
        }

        private static string ViewTypeLabel(ViewType type) => type switch
        {
            ViewType.FloorPlan => "Floor Plan",
            ViewType.CeilingPlan => "Ceiling Plan",
            ViewType.EngineeringPlan => "Structural Plan",
            ViewType.AreaPlan => "Area Plan",
            ViewType.DraftingView => "Drafting View",
            ViewType.Detail => "Detail View",
            ViewType.DrawingSheet => "Sheet",
            ViewType.ThreeD => "3D View",
            _ => type.ToString()
        };
    }
}
