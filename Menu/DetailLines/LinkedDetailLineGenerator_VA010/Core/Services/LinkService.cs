using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Services
{
    /// <summary>
    /// Queries the host document for placed RevitLinkInstances, and queries each
    /// linked document for available Category → Family → Type hierarchy.
    ///
    /// VA007 changes from VA006:
    ///  - GetLinkedModels excludes non-loaded links entirely (no "Unloaded" row at
    ///    all) and surfaces RevitLinkType.GetLinkedFileStatus() so the window can
    ///    filter/sort by status among the links it does show.
    ///  - BuildElementTree accepts an optional processing boundary + per-link
    ///    transform: when supplied (Processing Scope's "Restrict to boundary" is
    ///    on), only instances whose bounding box intersects that boundary are
    ///    considered candidates — same SpatialFilterService test the processing
    ///    engines already use, just run one stage earlier.
    ///  - Every Type leaf also gets ElementMetrics (Width/Height/Perimeter/Area)
    ///    from a representative instance, backing the Categories panel's filters.
    /// </summary>
    public class LinkService
    {
        private readonly SpatialFilterService _spatialFilter = new();
        private readonly ElementMetricsService _metrics = new();

        /// <summary>Returns Loaded/CanBeUpgraded RevitLinkInstances in the host
        /// document, wrapped as LinkedModelItem for UI binding. Unloaded, NotFound,
        /// InClosedWorkset, LocallyUnloaded and Imported links are excluded entirely —
        /// they never appear in the window at all, per the enhancement spec.</summary>
        public List<LinkedModelItem> GetLinkedModels(Document hostDoc, Action<string>? onLog = null)
        {
            var result = new List<LinkedModelItem>();

            var linkInstances = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            onLog?.Invoke($"Found {linkInstances.Count} RevitLinkInstance element(s) in host document");

            foreach (var li in linkInstances)
            {
                Document? linkedDoc = null;
                try
                {
                    linkedDoc = li.GetLinkDocument();
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"Link instance {li.Id.Value} — failed to access linked document: {ex.Message}");
                }

                if (linkedDoc == null)
                {
                    onLog?.Invoke($"Link instance {li.Id.Value} — not loaded, excluded from the list.");
                    continue;
                }

                LinkedFileStatus status = LinkedFileStatus.Loaded;
                try
                {
                    if (hostDoc.GetElement(li.GetTypeId()) is RevitLinkType linkType)
                        status = linkType.GetLinkedFileStatus();
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"Link instance {li.Id.Value} — failed to read link status: {ex.Message}");
                }

                string docTitle = linkedDoc.Title;
                string instanceName = li.Name ?? docTitle;

                result.Add(new LinkedModelItem
                {
                    LinkInstanceId = li.Id.Value,
                    InstanceName = instanceName,
                    DocumentTitle = docTitle.EndsWith(".rvt") ? docTitle : docTitle + ".rvt",
                    IsLoaded = true,
                    Status = status,
                    AccState = AccLinkStatus.Read(li)
                });
            }

            return result;
        }

        /// <summary>
        /// Builds the merged Category → Family → Type tree for all currently-loaded
        /// links whose LinkInstanceId is in selectedLinkIds. When scopeBoundary is
        /// non-null (Processing Scope's "Restrict to boundary" is on), each link's
        /// candidate instances are pre-filtered to those whose bounding box
        /// intersects the boundary — Types with zero surviving instances are
        /// omitted from the tree entirely, matching the spec's "filtering/selection
        /// is restricted to elements found within the selected Floor/Roof boundary"
        /// requirement. When scopeBoundary is null, every instance in the link is a
        /// candidate (matches VA006 behavior / "OFF" scope).
        /// </summary>
        public List<LinkTreeNode> BuildElementTree(
            Document hostDoc,
            IEnumerable<long> selectedLinkIds,
            List<XYZ>? scopeBoundary,
            Action<string>? onLog = null)
        {
            var nodes = new List<LinkTreeNode>();
            var selectedSet = selectedLinkIds.ToHashSet();

            var linkInstances = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(li => selectedSet.Contains(li.Id.Value))
                .ToList();

            foreach (var li in linkInstances)
            {
                Document? linkedDoc;
                try
                {
                    linkedDoc = li.GetLinkDocument();
                }
                catch
                {
                    continue;
                }
                if (linkedDoc == null) continue;

                Transform linkToHost = li.GetTotalTransform();

                var node = new LinkTreeNode
                {
                    LinkInstanceId = li.Id.Value,
                    LinkDisplayName = (li.Name ?? linkedDoc.Title) + (linkedDoc.Title.EndsWith(".rvt") ? "" : ".rvt")
                };

                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_Floors, "Floors", RepresentationGroup.Profile, onLog));
                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_Roofs, "Roofs", RepresentationGroup.Profile, onLog));

                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_Walls, "Walls", RepresentationGroup.Linear, onLog));
                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_StructuralFraming, "Structural Framing", RepresentationGroup.Linear, onLog));

                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_StructuralColumns, "Structural Columns", RepresentationGroup.Point, onLog));
                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_Columns, "Columns", RepresentationGroup.Point, onLog));
                node.Categories.Add(BuildCategoryNode(linkedDoc, linkToHost, scopeBoundary, BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment", RepresentationGroup.Point, onLog));

                // Only keep categories that actually had matching Types in this link
                node.Categories = new ObservableCollection<CategoryTreeItem>(
                    node.Categories.Where(c => c.Families.Count > 0));

                if (node.Categories.Count > 0)
                    nodes.Add(node);
            }

            return nodes;
        }

        private CategoryTreeItem BuildCategoryNode(
            Document linkedDoc, Transform linkToHost, List<XYZ>? scopeBoundary,
            BuiltInCategory bic, string displayName,
            RepresentationGroup group, Action<string>? onLog)
        {
            var catItem = new CategoryTreeItem { CategoryName = displayName, Group = group };

            List<Element> instances;
            try
            {
                instances = new FilteredElementCollector(linkedDoc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToList();
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Failed to collect {displayName} from linked doc '{linkedDoc.Title}': {ex.Message}");
                return catItem;
            }

            int totalFound = instances.Count;

            // VA007 Processing Scope: pre-filter to instances inside the boundary
            // before any Category/Family/Type grouping happens, so a Type with no
            // in-scope instances never appears in the tree at all.
            if (scopeBoundary != null && instances.Count > 0)
                instances = _spatialFilter.FilterCandidates(instances, linkToHost, scopeBoundary, linkedDoc);

            onLog?.Invoke(scopeBoundary != null
                ? $"{displayName}: {instances.Count}/{totalFound} instance(s) in '{linkedDoc.Title}' fall within the processing boundary"
                : $"{displayName}: {instances.Count} instance(s) found in '{linkedDoc.Title}'");

            // Group by (FamilyName, TypeName) — one TypeTreeItem per distinct type,
            // one FamilyTreeItem per distinct family name. Element (the instance) is
            // carried through so a representative instance is available for metrics.
            //
            // System families (Wall, Structural Framing when hosted as system types)
            // have no SYMBOL_FAMILY_NAME_PARAM — WallType exposes no family symbol,
            // so those fall back to the category name as a single family bucket
            // (e.g. all wall types grouped under "Walls"), matching how Revit's own
            // Project Browser groups system-family types. This is intentional, not
            // an oversight — loadable families (most floors/roofs, and beams that
            // use structural framing families) still group by their true family name.
            var withType = instances
                .Select(e => new { Element = e, TypeId = e.GetTypeId() })
                .Where(x => x.TypeId != ElementId.InvalidElementId)
                .Select(x => new { x.Element, x.TypeId, TypeElem = linkedDoc.GetElement(x.TypeId) })
                .Where(x => x.TypeElem != null)
                .ToList();

            var familyGroups = withType
                .GroupBy(x => x.TypeElem!.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString()
                              ?? x.TypeElem!.Category?.Name ?? "Unknown Family")
                .ToList();

            foreach (var famGroup in familyGroups)
            {
                var famItem = new FamilyTreeItem { FamilyName = famGroup.Key };

                var distinctTypes = famGroup
                    .GroupBy(x => x.TypeId.Value)
                    .ToList();

                foreach (var typeGroup in distinctTypes)
                {
                    var first = typeGroup.First();
                    ElementMetrics metrics = _metrics.ComputeMetrics(first.Element, group);

                    famItem.Types.Add(new TypeTreeItem
                    {
                        TypeId = first.TypeId.Value,
                        TypeName = first.TypeElem!.Name,
                        CategoryName = displayName,
                        FamilyName = famGroup.Key,
                        Group = group,
                        Metrics = metrics
                    });
                }

                if (famItem.Types.Count > 0)
                    catItem.Families.Add(famItem);
            }

            return catItem;
        }
    }
}
