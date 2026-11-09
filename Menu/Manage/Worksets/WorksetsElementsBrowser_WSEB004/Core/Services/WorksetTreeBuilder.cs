using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Models;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Services
{
    /// <summary>
    /// Builds the Workset -> Category -> Type hierarchy for the host document
    /// (linked-model elements are never included — out of scope for WSEB002).
    /// A model with no worksharing enabled reports every element under the
    /// implicit "Workset1" the API still returns via ELEM_PARTITION_PARAM.
    /// </summary>
    public class WorksetTreeBuilder
    {
        public List<WorksetNodeData> Build(Document doc)
        {
            var worksetsById = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToDictionary(ws => ws.Id.IntegerValue, ws => ws);

            // Every model element that can carry a workset assignment and sit
            // in a browsable category (skips element types themselves).
            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WhereElementIsViewIndependent()
                .Where(e => e.Category != null && e.Category.Id != new ElementId(BuiltInCategory.OST_RvtLinks))
                .ToList();

            // worksetId -> categoryName -> typeName -> element ids
            var grouped = new Dictionary<int, Dictionary<string, Dictionary<string, List<ElementId>>>>();

            foreach (var element in elements)
            {
                ToolCancellation.CheckCurrent();
                var worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (worksetParam == null || !worksetParam.HasValue) continue;

                int worksetId = worksetParam.AsInteger();
                if (!worksetsById.ContainsKey(worksetId)) continue; // project-standard/view worksets excluded

                string categoryName = element.Category.Name;
                string typeName = ResolveTypeName(doc, element);

                if (!grouped.TryGetValue(worksetId, out var byCategory))
                {
                    byCategory = new Dictionary<string, Dictionary<string, List<ElementId>>>();
                    grouped[worksetId] = byCategory;
                }
                if (!byCategory.TryGetValue(categoryName, out var byType))
                {
                    byType = new Dictionary<string, List<ElementId>>();
                    byCategory[categoryName] = byType;
                }
                if (!byType.TryGetValue(typeName, out var ids))
                {
                    ids = new List<ElementId>();
                    byType[typeName] = ids;
                }
                ids.Add(element.Id);
            }

            var result = new List<WorksetNodeData>();
            foreach (var ws in worksetsById.Values.OrderBy(w => w.Name))
            {
                ToolCancellation.CheckCurrent();
                List<CategoryNodeData> categories;
                if (grouped.TryGetValue(ws.Id.IntegerValue, out var byCategory))
                {
                    categories = byCategory
                        .OrderBy(c => c.Key)
                        .Select(c => new CategoryNodeData(
                            c.Key,
                            c.Value
                                .OrderBy(t => t.Key)
                                .Select(t => new TypeNodeData(t.Key, t.Value))
                                .ToList()))
                        .ToList();
                }
                else
                {
                    categories = new List<CategoryNodeData>();
                }

                result.Add(new WorksetNodeData(
                    ws.Name,
                    ws.Id.IntegerValue,
                    ws.IsEditable,
                    ws.Owner ?? string.Empty,
                    categories));
            }

            return result;
        }

        /// <summary>
        /// "Type" for grouping purposes: the element's own ElementType name
        /// where one exists (family+type name), otherwise the element's own
        /// Name (covers system-family-less elements without a distinct type).
        /// </summary>
        private static string ResolveTypeName(Document doc, Element element)
        {
            var typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                var elementType = doc.GetElement(typeId) as ElementType;
                if (elementType != null)
                {
                    string familyName = elementType.FamilyName;
                    return string.IsNullOrWhiteSpace(familyName)
                        ? elementType.Name
                        : $"{familyName} — {elementType.Name}";
                }
            }
            return string.IsNullOrWhiteSpace(element.Name) ? "(unnamed)" : element.Name;
        }
    }
}
