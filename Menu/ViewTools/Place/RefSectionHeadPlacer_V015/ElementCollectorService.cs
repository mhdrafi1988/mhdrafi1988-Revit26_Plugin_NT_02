using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Models;

namespace Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Services
{
    /// <summary>
    /// Loads document data for the grids/selectors:
    ///   • Plan views (host)        -> Grid 1
    ///   • Link instances (host)    -> "Linked Files" checkbox list
    ///   • Section types (host)     -> "Section type" ComboBox (via GeometryHelper)
    ///   • Drafting views (host)    -> Grid 3 mapping targets
    ///   • Roof type names (host)   -> "Roof type" filter popover (row 3)
    ///   • Element types            -> Grid 2:
    ///         Roofs = HOST document, scoped to selected plan views AND ticked Roof Types
    ///           (collected once per scan by CollectRoofsInViews — V015 15.3.0)
    ///         Doors / Walls / Plumbing Fixtures / Plumbing Equipment = EACH selected
    ///           linked file, scoped to a user-entered buffer (mm) around a qualifying
    ///           host roof's bounding box. Grouped PER LINK — two links both having
    ///           e.g. "Floor Drain 100mm" produce TWO separate Grid 2 rows, never
    ///           merged (confirmed with Rafi).
    /// </summary>
    public class ElementCollectorService
    {
        private static readonly BuiltInCategory[] LinkCategories =
        {
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_PlumbingEquipment
        };

        private const string HostSourceLabel = "Host";

        private readonly Document _hostDoc;

        public ElementCollectorService(Document hostDoc)
        {
            _hostDoc = hostDoc;
        }

        public List<PlanViewRow> LoadPlanViews()
        {
            return new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate)
                .Select(v => new PlanViewRow(v, v.ViewType.ToString(), v.GenLevel?.Name ?? "-"))
                .ToList();
        }

        public List<DraftingViewOption> LoadDraftingViews()
        {
            return new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.Name)
                .Select(v => new DraftingViewOption(v))
                .ToList();
        }

        /// <summary>All loaded Revit links, for the "Linked Files" checkbox list.</summary>
        public List<LinkRow> LoadLinkInstances()
        {
            return new FilteredElementCollector(_hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(li => new LinkRow(li))
                .Where(r => r.LinkDocument != null) // skip unloaded links
                .OrderBy(r => r.Name)
                .ToList();
        }

        /// <summary>
        /// V015 (15.3.0) PERF: collects the distinct host roofs visible in the selected plan
        /// views ONCE per scan. Before 15.3.0 the same view-scoped search ran three times per
        /// view (type names, raw count, Grid 2 rows); view-scoped collectors are slow because
        /// Revit evaluates visibility, so that tripled the scan time on models with many views.
        /// Feed the result to <see cref="GetRoofTypeNames"/> and <see cref="CollectElementTypes"/>.
        /// </summary>
        public List<Element> CollectRoofsInViews(IEnumerable<PlanViewRow> selectedViews)
        {
            var byId = new Dictionary<ElementId, Element>();
            foreach (var viewRow in selectedViews)
                foreach (var e in Collect(_hostDoc, viewRow.RevitView.Id, BuiltInCategory.OST_Roofs))
                    byId[e.Id] = e;
            return byId.Values.ToList();
        }

        /// <summary>Distinct roof type names among the given roofs (from
        /// <see cref="CollectRoofsInViews"/>) — populates the Roof Type filter popover.</summary>
        public List<string> GetRoofTypeNames(IEnumerable<Element> roofsInViews) =>
            roofsInViews.Select(GetTypeName).Distinct().OrderBy(n => n).ToList();

        /// <summary>
        /// Builds Grid 2. Host roofs (already scoped to the selected plan views by
        /// <see cref="CollectRoofsInViews"/>) are narrowed to the ticked Roof Types
        /// (allowedRoofTypeNames) — unticked roof types are excluded from both the Roofs rows
        /// and the boundary-buffer anchor set. For EACH checked link, doors/plumbing/walls/
        /// plumbing equipment within bufferMm of a qualifying roof's bounding box are collected
        /// into that link's OWN rows (never merged).
        /// </summary>
        public List<ElementTypeRow> CollectElementTypes(
            IReadOnlyCollection<Element> roofsInViews,
            IEnumerable<LinkRow> selectedLinks,
            IReadOnlyCollection<string> allowedRoofTypeNames,
            double bufferMm)
        {
            var rows = new List<ElementTypeRow>();
            var bufferFeet = UnitUtils.ConvertToInternalUnits(bufferMm, UnitTypeId.Millimeters);

            // ── Host roofs, narrowed by ticked roof types ──
            var hostRoofElements = roofsInViews
                .Where(e => allowedRoofTypeNames.Contains(GetTypeName(e)))
                .ToList();
            rows.AddRange(GroupToRows(hostRoofElements, BuiltInCategory.OST_Roofs, HostSourceLabel,
                _hostDoc, Transform.Identity, false));

            if (hostRoofElements.Count == 0)
                return SortRows(rows);

            // Expanded roof bounding boxes (HOST coords), computed once.
            var expandedBoxes = hostRoofElements
                .Select(r => r.get_BoundingBox(null))
                .Where(b => b != null)
                .Select(b => (
                    Min: b.Min - new XYZ(bufferFeet, bufferFeet, bufferFeet),
                    Max: b.Max + new XYZ(bufferFeet, bufferFeet, bufferFeet)))
                .ToList();

            if (expandedBoxes.Count == 0)
                return SortRows(rows);

            // ── Each selected link independently — never merged across links ──
            foreach (var linkRow in selectedLinks)
            {
                if (linkRow.LinkDocument == null) continue;

                var linkDoc = linkRow.LinkDocument;
                var toHost = linkRow.Instance.GetTotalTransform();
                var prefilter = BuildLinkPrefilter(expandedBoxes, toHost.Inverse);

                foreach (var bic in LinkCategories)
                {
                    // Revit's bounding-box quick filter discards far-away elements before
                    // they are loaded; the exact representative-point test below still
                    // decides membership, so results match the pre-15.3.0 rule.
                    var inScope = Collect(linkDoc, null, bic)
                        .WherePasses(prefilter)
                        .Where(e =>
                        {
                            var p = GetRepresentativePointInHost(e, toHost);
                            return p != null && expandedBoxes.Any(b => IsInside(p, b.Min, b.Max));
                        })
                        .ToList();

                    rows.AddRange(GroupToRows(inScope, bic, linkRow.Name, linkDoc, toHost, true));
                }
            }

            return SortRows(rows);
        }

        /// <summary>
        /// Margin added around each roof box in the link prefilter. An element passes the
        /// exact test when its location point (or curve midpoint) is in a roof box, and that
        /// point normally lies inside the element's own bounding box; the margin covers families
        /// whose insertion point sits a little outside their geometry, so the quick filter never
        /// drops an element the exact test would keep.
        /// </summary>
        private const double PrefilterMarginFeet = 3.0; // ≈ 914 mm

        /// <summary>
        /// One <see cref="BoundingBoxIntersectsFilter"/> per expanded roof box, moved into the
        /// link's coordinates (all 8 corners transformed, then re-boxed, so rotated links stay
        /// covered), OR-ed together.
        /// </summary>
        private static ElementFilter BuildLinkPrefilter(
            IReadOnlyList<(XYZ Min, XYZ Max)> hostBoxes, Transform hostToLink)
        {
            var filters = new List<ElementFilter>(hostBoxes.Count);
            foreach (var (min, max) in hostBoxes)
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
                double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
                foreach (var x in new[] { min.X, max.X })
                    foreach (var y in new[] { min.Y, max.Y })
                        foreach (var z in new[] { min.Z, max.Z })
                        {
                            var p = hostToLink.OfPoint(new XYZ(x, y, z));
                            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
                        }
                var outline = new Outline(new XYZ(x0, y0, z0), new XYZ(x1, y1, z1));
                filters.Add(new BoundingBoxIntersectsFilter(outline, PrefilterMarginFeet));
            }
            return filters.Count == 1 ? filters[0] : new LogicalOrFilter(filters);
        }

        // ── helpers ───────────────────────────────────────────────────

        private static FilteredElementCollector Collect(Document doc, ElementId viewId, BuiltInCategory bic)
        {
            var collector = viewId == null
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, viewId);
            return collector.OfCategory(bic).WhereElementIsNotElementType();
        }

        /// <summary>
        /// Groups elements of a SINGLE category by type name into rows. All elements
        /// share the same BuiltInCategory (bic) and source, so those are stamped on
        /// every row; the display Category name is read from the elements.
        /// </summary>
        private static IEnumerable<ElementTypeRow> GroupToRows(
            IReadOnlyCollection<Element> elements, BuiltInCategory bic, string sourceLabel,
            Document doc, Transform toHost, bool isFromLink)
        {
            var groups = new Dictionary<string, List<ElementId>>();
            string categoryName = bic.ToString();

            foreach (var e in elements)
            {
                categoryName = e.Category?.Name ?? categoryName;
                var type = GetTypeName(e);
                if (!groups.TryGetValue(type, out var ids))
                {
                    ids = new List<ElementId>();
                    groups[type] = ids;
                }
                ids.Add(e.Id); // collector never yields duplicates within one query
            }

            var cat = categoryName;
            return groups.Select(kvp =>
                new ElementTypeRow(sourceLabel, cat, kvp.Key, bic, kvp.Value, doc, toHost, isFromLink));
        }

        private static List<ElementTypeRow> SortRows(List<ElementTypeRow> rows) =>
            rows.OrderBy(r => r.SourceLabel).ThenBy(r => r.Category).ThenBy(r => r.TypeName).ToList();

        private static XYZ GetRepresentativePointInHost(Element e, Transform toHost)
        {
            XYZ localPoint = e.Location switch
            {
                LocationPoint lp => lp.Point,
                LocationCurve lc => lc.Curve.Evaluate(0.5, true),
                _ => null
            };
            return localPoint == null ? null : toHost.OfPoint(localPoint);
        }

        private static bool IsInside(XYZ point, XYZ min, XYZ max) =>
            point.X >= min.X && point.X <= max.X &&
            point.Y >= min.Y && point.Y <= max.Y &&
            point.Z >= min.Z && point.Z <= max.Z;

        /// <summary>
        /// V012 FIX: previously fell back to elem.Name when the type lookup came
        /// up empty. For a Wall INSTANCE, elem.Name is NOT a type name — it's
        /// Revit's internal location-line descriptor (e.g. "Location &lt;Not
        /// Shared&gt;"), which was leaking into Grid 3's Category/Type display
        /// as garbage phantom rows. Now: always resolve through the TYPE element
        /// (GetTypeId -> that element's Name). Only if the type element itself
        /// has no name do we fall back to a stable, honest placeholder — never
        /// the source instance's own .Name.
        /// </summary>
        private static string GetTypeName(Element elem)
        {
            var typeId = elem.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
                return $"{elem.Category?.Name ?? "Type"} {elem.Id.Value}";

            var typeElem = elem.Document.GetElement(typeId);
            return !string.IsNullOrWhiteSpace(typeElem?.Name)
                ? typeElem.Name
                : $"{elem.Category?.Name ?? "Type"} {typeId.Value}";
        }
    }
}
