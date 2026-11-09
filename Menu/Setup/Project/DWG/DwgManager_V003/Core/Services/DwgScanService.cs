using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DwgManager.V003.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgManager.V003.Core.Services
{
    /// <summary>
    /// Reads the CAD links and imports of a document. Every method must run in a Revit API context
    /// (the command, or the tool's external event).
    /// </summary>
    public static class DwgScanService
    {
        /// <summary>Label of the group for instances that are not owned by a view.</summary>
        public const string ModelLevelCategory = "Model Level (All Views)";

        /// <summary>
        /// Lists every CAD instance in <paramref name="doc"/>, one row per CAD file per owner view.
        /// Instances whose type cannot be read are skipped and reported through <paramref name="log"/>.
        /// </summary>
        public static List<DwgItemModel> Scan(Document doc, Action<LogEntry> log)
        {
            var result = new List<DwgItemModel>();
            if (doc == null) return result;

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();

            var typeNames = new Dictionary<ElementId, (string Name, string Path)>();
            int unreadable = 0;

            foreach (var group in instances.GroupBy(i => (TypeId: i.GetTypeId(), ViewId: i.OwnerViewId ?? ElementId.InvalidElementId)))
            {
                ToolCancellation.CheckCurrent();
                var typeId = group.Key.TypeId;
                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    unreadable += group.Count();
                    continue;
                }

                if (!typeNames.TryGetValue(typeId, out var file))
                {
                    file = ReadFileName(doc, typeId);
                    typeNames[typeId] = file;
                }

                if (file.Name == null)
                {
                    unreadable += group.Count();
                    continue;
                }

                string viewName = "(all views)";
                string category = ModelLevelCategory;
                var viewId = group.Key.ViewId;
                if (viewId != ElementId.InvalidElementId)
                {
                    if (doc.GetElement(viewId) is View view)
                    {
                        viewName = view.Name;
                        category = CategoryLabel(view.ViewType);
                    }
                    else
                    {
                        viewName = $"(view #{viewId.Value})";
                        category = "Other Views";
                    }
                }

                var first = group.First();
                result.Add(new DwgItemModel
                {
                    TypeId = typeId,
                    OwnerViewId = viewId,
                    FileName = file.Name,
                    FilePath = file.Path,
                    ViewName = viewName,
                    ViewCategory = category,
                    IsLinked = first.IsLinked,
                    InstanceIds = group.Select(i => i.Id).ToList()
                });
            }

            if (unreadable > 0)
                log?.Invoke(new LogEntry(LogLevel.Warning, $"{unreadable} CAD instance(s) skipped: their type could not be read."));

            return result
                .OrderByDescending(r => r.IsLinked)
                .ThenBy(r => r.ViewCategory, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ViewName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// CAD instances Revit draws in <paramref name="activeView"/> (owned by it, or model-level and not
        /// hidden there). Views that cannot be collected (schedules, browsers) give an empty set.
        /// </summary>
        public static HashSet<ElementId> GetVisibleInstanceIds(Document doc, View activeView)
        {
            var ids = new HashSet<ElementId>();
            if (doc == null || activeView == null || !activeView.IsValidObject) return ids;

            try
            {
                foreach (var id in new FilteredElementCollector(doc, activeView.Id)
                             .OfClass(typeof(ImportInstance))
                             .ToElementIds())
                {
                    ids.Add(id);
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The active view cannot hold CAD (schedule, project browser…): nothing is visible.
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Same as above for view kinds Revit refuses to collect from.
            }

            return ids;
        }

        // File name (with its real extension) and, for links, the full path.
        private static (string Name, string Path) ReadFileName(Document doc, ElementId typeId)
        {
            if (doc.GetElement(typeId) is not ElementType type) return (null, null);

            string name = type.Name;
            string path = string.Empty;

            try
            {
                if (ExternalFileUtils.IsExternalFileReference(doc, typeId))
                {
                    var reference = ExternalFileUtils.GetExternalFileReference(doc, typeId);
                    var modelPath = reference?.GetAbsolutePath();
                    if (modelPath != null)
                    {
                        path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(name) && path.Length > 0)
                            name = global::System.IO.Path.GetFileName(path);
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Path unavailable (unresolved link, cloud path): the type name is enough.
            }

            return (string.IsNullOrWhiteSpace(name) ? $"(CAD type #{typeId.Value})" : name, path);
        }

        private static string CategoryLabel(ViewType viewType)
        {
            switch (viewType)
            {
                case ViewType.FloorPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                    return "Floor Plans";
                case ViewType.CeilingPlan:
                    return "Ceiling Plans";
                case ViewType.Elevation:
                    return "Elevations";
                case ViewType.Section:
                    return "Sections";
                case ViewType.Detail:
                    return "Detail Views";
                case ViewType.ThreeD:
                    return "3D Views";
                case ViewType.DraftingView:
                    return "Drafting Views";
                case ViewType.Legend:
                    return "Legends";
                case ViewType.DrawingSheet:
                    return "Sheets";
                default:
                    return "Other Views";
            }
        }
    }
}
