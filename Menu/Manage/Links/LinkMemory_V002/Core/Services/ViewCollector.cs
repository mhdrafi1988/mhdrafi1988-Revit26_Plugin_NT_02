using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Services
{
    /// <summary>A view picked by the collector, with why it was picked.</summary>
    public sealed class CollectedView
    {
        /// <summary>The view or sheet.</summary>
        public View View { get; init; }

        /// <summary>Why it was collected.</summary>
        public string Reason { get; init; }

        /// <summary>Indent level in the list.</summary>
        public int Depth { get; init; }
    }

    /// <summary>
    /// Finds the views a memory covers and reads which links are visible in them. Read only:
    /// nothing in the model changes.
    /// <list type="bullet">
    /// <item>Active plan: the plan, plus the sections, elevations, callouts and drafting views whose
    /// marks (or reference marks) show in it.</item>
    /// <item>Active section, elevation, callout or drafting view: the view, the views whose marks show
    /// in it, and the plans that show its own mark (callouts and drafting views: any plan or section).</item>
    /// <item>Active sheet: the sheet, every view on it, and the related views of each.</item>
    /// <item>Print set: every sheet and view in it, plus the views placed on its sheets.</item>
    /// </list>
    /// </summary>
    public sealed class ViewCollector
    {
        private readonly Document _doc;
        private readonly CollectOptions _options;
        private readonly Action<LogEntry> _log;
        private Dictionary<ElementId, ElementId> _viewerToView;
        private readonly Dictionary<ElementId, List<View>> _marksCache = new();

        /// <summary>Creates a collector for <paramref name="doc"/>.</summary>
        public ViewCollector(Document doc, CollectOptions options, Action<LogEntry> log)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _options = options ?? new CollectOptions();
            _log = log ?? (_ => { });
        }

        /// <summary>True when links can be read from <paramref name="view"/> (graphical views and sheets).</summary>
        public static bool IsCollectable(View view)
            => view != null && view.IsValidObject && !view.IsTemplate && view.ViewType switch
            {
                ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan
                    or ViewType.Section or ViewType.Elevation or ViewType.Detail or ViewType.DraftingView
                    or ViewType.ThreeD or ViewType.DrawingSheet => true,
                _ => false
            };

        /// <summary>Display text of a view's type.</summary>
        public static string TypeText(View view)
        {
            if (view.IsCallout) return "Callout";
            return view.ViewType switch
            {
                ViewType.FloorPlan => "Floor plan",
                ViewType.CeilingPlan => "Ceiling plan",
                ViewType.EngineeringPlan => "Structural plan",
                ViewType.AreaPlan => "Area plan",
                ViewType.Section => "Section",
                ViewType.Elevation => "Elevation",
                ViewType.Detail => "Detail",
                ViewType.DraftingView => "Drafting view",
                ViewType.ThreeD => "3D view",
                ViewType.DrawingSheet => "Sheet",
                _ => view.ViewType.ToString()
            };
        }

        /// <summary>Display name: sheets as "number – name".</summary>
        public static string NameOf(View view)
            => view is ViewSheet sheet ? $"{sheet.SheetNumber} – {sheet.Name}" : view.Name;

        /// <summary>The active view and the views connected to it.</summary>
        public List<CollectedView> ForView(View active)
        {
            var result = new List<CollectedView>();
            var seen = new HashSet<ElementId>();
            if (!IsCollectable(active)) return result;

            Add(result, seen, active, "Active view", 0);

            if (active is ViewSheet sheet)
            {
                foreach (View placed in PlacedViews(sheet))
                {
                    if (!Add(result, seen, placed, "On sheet", 1)) continue;
                    foreach (View related in MarksIn(placed).Where(Wanted))
                        Add(result, seen, related, $"Mark in {placed.Name}", 2);
                }
                return result;
            }

            foreach (View related in MarksIn(active).Where(Wanted))
                Add(result, seen, related, "Mark in this view", 1);

            if (active.IsCallout || active.ViewType is ViewType.Section or ViewType.Elevation
                    or ViewType.Detail or ViewType.DraftingView)
            {
                foreach (View parent in ViewsShowingMarkOf(active))
                    Add(result, seen, parent, "Shows this view's mark", 1);
            }
            return result;
        }

        /// <summary>Every sheet and view of <paramref name="set"/>, plus the views placed on its sheets (if chosen).</summary>
        public List<CollectedView> ForPrintSet(ViewSheetSet set)
        {
            var result = new List<CollectedView>();
            var seen = new HashSet<ElementId>();
            var members = new List<View>();
            foreach (View v in set.Views) members.Add(v);

            foreach (View v in members.OrderBy(v => v is ViewSheet ? 0 : 1)
                                      .ThenBy(v => NameOf(v), StringComparer.OrdinalIgnoreCase))
            {
                if (!IsCollectable(v))
                {
                    _log(new LogEntry(LogLevel.Info, $"Skipped {v.Name}: a {v.ViewType} can't show links."));
                    continue;
                }
                if (!Add(result, seen, v, "In print set", 0)) continue;
                if (v is ViewSheet sheet && _options.PlacedViews)
                {
                    foreach (View placed in PlacedViews(sheet))
                        Add(result, seen, placed, $"On sheet {sheet.SheetNumber}", 1);
                }
            }
            return result;
        }

        /// <summary>
        /// UniqueIds of the link / import types visible in <paramref name="view"/>: the instance is
        /// returned by Revit's view filter (category visibility, hidden elements, view range and crop)
        /// and is not hidden in the view.
        /// </summary>
        public HashSet<string> VisibleLinkTypes(View view)
        {
            var ids = new HashSet<string>();
            var filter = new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementClassFilter(typeof(RevitLinkInstance)),
                new ElementClassFilter(typeof(ImportInstance)),
                new ElementClassFilter(typeof(PointCloudInstance)),
                new ElementClassFilter(typeof(ImageInstance))
            });

            IEnumerable<Element> elements;
            try
            {
                elements = new FilteredElementCollector(_doc, view.Id).WherePasses(filter).WhereElementIsNotElementType().ToElements();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                _log(new LogEntry(LogLevel.Warning, $"Could not read links in {view.Name}: {ex.Message}"));
                return ids;
            }

            foreach (Element e in elements)
            {
                try
                {
                    if (e.IsHidden(view)) continue;
                    Category cat = e.Category;
                    if (cat != null && view.CanCategoryBeHidden(cat.Id) && view.GetCategoryHidden(cat.Id)) continue;
                    Element type = _doc.GetElement(e.GetTypeId());
                    if (type != null) ids.Add(type.UniqueId);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // An element that can't be checked is left out rather than guessed.
                }
            }
            return ids;
        }

        // ── related views ─────────────────────────────────────────────────

        private bool Add(List<CollectedView> list, HashSet<ElementId> seen, View view, string reason, int depth)
        {
            if (!IsCollectable(view) || !seen.Add(view.Id)) return false;
            list.Add(new CollectedView { View = view, Reason = reason, Depth = depth });
            return true;
        }

        private static bool IsPlan(View v)
            => v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan;

        // Is a view reached through a mark of a kind the user asked for?
        private bool Wanted(View v)
        {
            if (v.IsCallout || v.ViewType == ViewType.Detail) return _options.Callouts;
            return v.ViewType switch
            {
                ViewType.Section => _options.Sections,
                ViewType.Elevation => _options.Elevations,
                ViewType.DraftingView => _options.DraftingViews,
                _ => false
            };
        }

        private IEnumerable<View> PlacedViews(ViewSheet sheet)
        {
            var views = new List<View>();
            try
            {
                foreach (ElementId id in sheet.GetAllPlacedViews())
                    if (_doc.GetElement(id) is View v && IsCollectable(v)) views.Add(v);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                _log(new LogEntry(LogLevel.Warning, $"Could not read the views on {sheet.SheetNumber}: {ex.Message}"));
            }
            return views.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Views whose marks (section, elevation, callout or reference marks) show in <paramref name="view"/>.</summary>
        private List<View> MarksIn(View view)
        {
            if (_marksCache.TryGetValue(view.Id, out List<View> cached)) return cached;

            var targets = new Dictionary<ElementId, View>();
            void Take(ElementId id)
            {
                if (id == null || id == ElementId.InvalidElementId || id == view.Id || targets.ContainsKey(id)) return;
                if (_doc.GetElement(id) is View v && IsCollectable(v)) targets[id] = v;
            }

            if (view is not ViewSheet && view.ViewType != ViewType.DraftingView)
            {
                try
                {
                    // Section, callout and elevation viewer elements visible in the view.
                    Dictionary<ElementId, ElementId> viewerToView = ViewerIndex();
                    foreach (ElementId viewerId in new FilteredElementCollector(_doc, view.Id)
                                 .OfCategory(BuiltInCategory.OST_Viewers).WhereElementIsNotElementType().ToElementIds())
                    {
                        if (viewerToView.TryGetValue(viewerId, out ElementId target)) Take(target);
                    }

                    // Elevation markers visible in the view.
                    foreach (ElevationMarker marker in new FilteredElementCollector(_doc, view.Id)
                                 .OfClass(typeof(ElevationMarker)).Cast<ElevationMarker>())
                    {
                        for (int i = 0; i < marker.MaximumViewCount; i++)
                            Take(marker.GetViewId(i));   // InvalidElementId for an empty slot
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    _log(new LogEntry(LogLevel.Warning, $"Could not read the marks in {view.Name}: {ex.Message}"));
                }
            }

            // Reference sections, callouts and elevations (these can point at drafting views).
            try
            {
                foreach (ElementId refId in view.GetReferenceSections()
                             .Concat(view.GetReferenceCallouts())
                             .Concat(view.GetReferenceElevations()))
                {
                    Take(ReferenceableViewUtils.GetReferencedViewId(_doc, refId));
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // View type without reference views.
            }

            List<View> list = targets.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _marksCache[view.Id] = list;
            return list;
        }

        /// <summary>Plans (and, for callouts and drafting views, sections and elevations) that show the mark of <paramref name="view"/>.</summary>
        private IEnumerable<View> ViewsShowingMarkOf(View view)
        {
            bool anyParent = view.IsCallout || view.ViewType is ViewType.Detail or ViewType.DraftingView;
            var candidates = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.Id != view.Id
                            && (IsPlan(v) || (anyParent && v.ViewType is ViewType.Section or ViewType.Elevation or ViewType.Detail)))
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase);

            foreach (View candidate in candidates)
            {
                if (MarksIn(candidate).Any(t => t.Id == view.Id)) yield return candidate;
            }
        }

        // Viewer element (the mark Revit draws) → its view, for every view that has one.
        private Dictionary<ElementId, ElementId> ViewerIndex()
        {
            if (_viewerToView != null) return _viewerToView;
            _viewerToView = new Dictionary<ElementId, ElementId>();
            var viewers = new ElementCategoryFilter(BuiltInCategory.OST_Viewers);

            foreach (View v in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
            {
                if (v.IsTemplate || v.ViewType is ViewType.DrawingSheet or ViewType.Schedule or ViewType.Legend
                        or ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Internal or ViewType.DraftingView)
                    continue;
                try
                {
                    foreach (ElementId viewerId in v.GetDependentElements(viewers))
                        _viewerToView[viewerId] = v.Id;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Skip views Revit can't query.
                }
            }
            return _viewerToView;
        }
    }
}
