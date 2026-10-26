using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>
    /// Collects detail lines for a scope and builds grid rows; <see cref="ScanJob"/> runs it in steps.
    /// Only detail lines of the host model are read (<c>CurveElementType.DetailCurve</c>: lines,
    /// arcs, splines, ellipses); linked models and sketch boundaries are left out.
    /// Call only in the Revit API context.
    /// </summary>
    public static class DetailLineScanner
    {
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

        /// <summary>Ids of the detail lines in <paramref name="scope"/>, sketch boundary lines left out.</summary>
        public static List<ElementId> CollectIds(UIDocument uiDoc, SearchScope scope, HashSet<ElementId> sketchMembers,
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

            var list = new List<ElementId>();
            sketchSkipped = 0;
            foreach (var e in source)
            {
                if (!IsDetailLine(e)) continue;
                if (sketchMembers.Contains(e.Id)) { sketchSkipped++; continue; }
                list.Add(e.Id);
            }
            return list;
        }

        /// <summary>
        /// Boundary lines of filled regions, masking regions and other sketches. They are detail curves
        /// too, but deleting or restyling them would break their sketch.
        /// </summary>
        public static HashSet<ElementId> CollectSketchMembers(Document doc)
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

        /// <summary>Lookups shared by every <see cref="BuildRow"/> call of one search.</summary>
        public sealed class RowCache
        {
            internal Dictionary<ElementId, (string Name, int Weight)> Styles { get; } = new();
            internal Dictionary<ElementId, View> Views { get; } = new();
            internal Dictionary<(long View, long Category), int> VgWeights { get; } = new();
            internal Dictionary<ElementId, string> GroupNames { get; } = new();
            internal ElementId LinesCategoryId { get; set; }
            internal bool LinesCategoryRead { get; set; }
        }

        /// <summary>Builds the grid row of <paramref name="curve"/>; <paramref name="geometry"/> is its curve.</summary>
        public static DetailLineRow BuildRow(Document doc, CurveElement curve, EditabilityService editability,
                                             RowCache cache, out Curve geometry)
        {
            var styleCache = cache.Styles;
            var viewCache = cache.Views;
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

            int vgWeight = view == null || styleCatId == null ? 0 : VgWeight(doc, view, styleCatId, cache);

            ElementId groupId = EditabilityService.InGroup(curve) ? curve.GroupId : null;
            string groupName = string.Empty;
            if (groupId != null && !cache.GroupNames.TryGetValue(groupId, out groupName))
                cache.GroupNames[groupId] = groupName = doc.GetElement(groupId)?.Name ?? string.Empty;

            return new DetailLineRow
            {
                Id = curve.Id,
                StyleName = styleName,
                StyleCategoryId = styleCatId,
                StyleWeight = styleWeight,
                OverrideWeight = overrideWeight,
                VgWeight = vgWeight,
                ViewId = viewId,
                ViewName = view?.Name ?? string.Empty,
                ViewTypeName = view == null ? string.Empty : ViewTypeLabel(view.ViewType),
                LengthMm = geometry == null ? 0 : UnitUtils.ConvertFromInternalUnits(geometry.Length, UnitTypeId.Millimeters),
                Curve = KindOf(geometry),
                WorksetName = editability.WorksetName(curve),
                Comment = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? string.Empty,
                GroupId = groupId,
                GroupName = groupName,
                OwnedBy = editability.OwnedByOther(curve)
            };
        }

        // Weight the view's Visibility/Graphics sets for the line style, falling back to the Lines
        // category it belongs to. When a view template controls V/G, the template's settings apply.
        private static int VgWeight(Document doc, View view, ElementId styleCatId, RowCache cache)
        {
            View source = view;
            try
            {
                if (view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId
                    && doc.GetElement(view.ViewTemplateId) is View template)
                {
                    var notControlled = template.GetNonControlledTemplateParameterIds();
                    var vgParam = new ElementId(BuiltInParameter.VIS_GRAPHICS_MODEL);
                    if (notControlled == null || !notControlled.Contains(vgParam)) source = template;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                source = view;
            }

            var key = (source.Id.Value, styleCatId.Value);
            if (cache.VgWeights.TryGetValue(key, out int weight)) return weight;

            if (!cache.LinesCategoryRead)
            {
                cache.LinesCategoryId = LineStyleService.GetLinesCategory(doc)?.Id;
                cache.LinesCategoryRead = true;
            }

            weight = ReadVgPen(source, styleCatId);
            if (weight == 0 && cache.LinesCategoryId != null) weight = ReadVgPen(source, cache.LinesCategoryId);
            cache.VgWeights[key] = weight;
            return weight;
        }

        private static int ReadVgPen(View view, ElementId categoryId)
        {
            try
            {
                int pen = view.GetCategoryOverrides(categoryId).ProjectionLineWeight;
                return pen != OverrideGraphicSettings.InvalidPenNumber && pen > 0 ? pen : 0;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 0; // view without category overrides (legends, schedules)
            }
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
