using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V004
{
    /// <summary>
    /// Queries the host document for placed RevitLinkInstances, and queries each
    /// linked document for its available Category → Family → Type hierarchy.
    /// GetLinkedModels is copied as-is from LinkedDetailLineGenerator_VA003's
    /// LinkService. BuildElementTree differs from that reference: instead of a
    /// fixed BuiltInCategory whitelist, it iterates every Model-type category
    /// actually present on non-type elements in the linked document (grouped by
    /// e.Category.Name; annotation/tag/view-specific categories are excluded via
    /// CategoryType.Model), reusing the same family-grouping trick (fall back to
    /// the category name when SYMBOL_FAMILY_NAME_PARAM is absent, e.g. system
    /// families).
    /// </summary>
    public class LinkService
    {
        public List<LinkedModelItem> GetLinkedModels(Document hostDoc, Action<string> onLog = null)
        {
            var result = new List<LinkedModelItem>();

            var linkInstances = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            onLog?.Invoke($"Found {linkInstances.Count} RevitLinkInstance element(s) in host document");

            foreach (var li in linkInstances)
            {
                Document linkedDoc = null;
                try
                {
                    linkedDoc = li.GetLinkDocument();
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"Link instance {li.Id.Value} — failed to access linked document: {ex.Message}");
                }

                string docTitle = linkedDoc?.Title ?? (li.Name ?? "Unknown");
                string instanceName = li.Name ?? docTitle;

                result.Add(new LinkedModelItem
                {
                    LinkInstanceId = li.Id.Value,
                    InstanceName = instanceName,
                    DocumentTitle = docTitle.EndsWith(".rvt") ? docTitle : docTitle + ".rvt",
                    IsLoaded = linkedDoc != null,
                    AccState = AccLinkStatus.Read(li)
                });
            }

            return result;
        }

        /// <summary>
        /// Builds the merged Category → Family → Type tree for all currently-loaded
        /// links whose LinkInstanceId is in selectedLinkIds — every category present
        /// in the linked document, not a fixed whitelist.
        /// </summary>
        public List<LinkTreeNode> BuildElementTree(
            Document hostDoc,
            IEnumerable<long> selectedLinkIds,
            Action<string> onLog = null)
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
                Document linkedDoc;
                try
                {
                    linkedDoc = li.GetLinkDocument();
                }
                catch
                {
                    continue;
                }
                if (linkedDoc == null) continue;

                var node = new LinkTreeNode
                {
                    LinkInstanceId = li.Id.Value,
                    LinkDisplayName = (li.Name ?? linkedDoc.Title) + (linkedDoc.Title.EndsWith(".rvt") ? "" : ".rvt")
                };

                List<Element> allInstances;
                try
                {
                    allInstances = new FilteredElementCollector(linkedDoc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null
                                 // HasMaterialQuantities is Revit's own semantic marker for "this
                                 // category represents real, quantifiable building material" — true
                                 // for Walls/Floors/Roofs/Doors/Mechanical Equipment/etc., false for
                                 // every administrative category (Sheets, Views, Cameras, RVT Links,
                                 // Levels, Grids, Reference Planes, Schedules, Scope Boxes, ...).
                                 // It's a category-level capability flag, independent of whether any
                                 // given instance actually has a material assigned — an unmaterialed
                                 // Floor still passes, since the Floor category itself qualifies.
                                 && e.Category.HasMaterialQuantities
                                 && e.GetTypeId() != ElementId.InvalidElementId)
                        .ToList();
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"Failed to collect elements from linked doc '{linkedDoc.Title}': {ex.Message}");
                    continue;
                }

                onLog?.Invoke($"{allInstances.Count} candidate element(s) found in '{linkedDoc.Title}' across all Model categories");

                var byCategory = allInstances.GroupBy(e => e.Category.Name);

                foreach (var catGroup in byCategory)
                {
                    var catItem = BuildCategoryNode(linkedDoc, catGroup.Key, catGroup.ToList());
                    if (catItem.Families.Count > 0)
                        node.Categories.Add(catItem);
                }

                if (node.Categories.Count > 0)
                    nodes.Add(node);
            }

            return nodes;
        }

        private CategoryTreeItem BuildCategoryNode(Document linkedDoc, string displayName, List<Element> instances)
        {
            var catItem = new CategoryTreeItem { CategoryName = displayName };

            // Group by (FamilyName, TypeName) — one TypeTreeItem per distinct type,
            // one FamilyTreeItem per distinct family name. System families (no
            // SYMBOL_FAMILY_NAME_PARAM) fall back to the category name as a single
            // family bucket, matching how Revit's own Project Browser groups
            // system-family types.
            var familyGroups = instances
                .Select(e => new { Element = e, TypeId = e.GetTypeId() })
                .Where(x => x.TypeId != ElementId.InvalidElementId)
                .Select(x => new { x.TypeId, TypeElem = linkedDoc.GetElement(x.TypeId) })
                .Where(x => x.TypeElem != null)
                .GroupBy(x => x.TypeElem.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString()
                              ?? x.TypeElem.Category?.Name ?? "Unknown Family")
                .ToList();

            foreach (var famGroup in familyGroups)
            {
                var famItem = new FamilyTreeItem { FamilyName = famGroup.Key };

                var distinctTypes = famGroup
                    .GroupBy(x => x.TypeId.Value)
                    .Select(g => g.First())
                    .ToList();

                foreach (var t in distinctTypes)
                {
                    int instanceCount = famGroup.Count(x => x.TypeId.Value == t.TypeId.Value);

                    famItem.Types.Add(new TypeTreeItem
                    {
                        TypeId = t.TypeId.Value,
                        TypeName = t.TypeElem.Name,
                        InstanceCount = instanceCount
                    });
                }

                if (famItem.Types.Count > 0)
                    catItem.Families.Add(famItem);
            }

            return catItem;
        }
    }
}
