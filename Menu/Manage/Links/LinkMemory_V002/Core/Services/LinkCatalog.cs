using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Services
{
    /// <summary>One linked or imported file of the open model, with its current state.</summary>
    public sealed class LiveLink
    {
        /// <summary>The link / import type element.</summary>
        public Element Type { get; init; }

        /// <summary>Kind of link.</summary>
        public LinkKind Kind { get; init; }

        /// <summary>Type name.</summary>
        public string Name { get; init; }

        /// <summary>UniqueId of the type.</summary>
        public string UniqueId { get; init; }

        /// <summary>User-visible path, or null.</summary>
        public string Path { get; init; }

        /// <summary>Link status for Revit, IFC and CAD links; null for kinds that have none.</summary>
        public LinkedFileStatus? Status { get; init; }

        /// <summary>True when the file is loaded (imports, point clouds and loaded images count as loaded).</summary>
        public bool IsLoaded { get; init; }

        /// <summary>Workset of the first instance, or empty.</summary>
        public string Workset { get; init; }

        /// <summary>True when the tool can load / unload this kind (Revit and IFC links).</summary>
        public bool CanUnload => Kind is LinkKind.Revit or LinkKind.Ifc;

        /// <summary>True when the tool can load this kind (Revit, IFC and CAD links).</summary>
        public bool CanLoad => Kind is LinkKind.Revit or LinkKind.Ifc or LinkKind.CadLink;
    }

    /// <summary>Reads every linked and imported file of a model: Revit, IFC, CAD, point clouds, images and PDFs.</summary>
    public static class LinkCatalog
    {
        /// <summary>True when <paramref name="pathOrName"/> names an IFC file.</summary>
        public static bool IsIfcPath(string pathOrName)
            => !string.IsNullOrEmpty(pathOrName) && pathOrName.TrimEnd().EndsWith(".ifc", StringComparison.OrdinalIgnoreCase);

        /// <summary>All links of <paramref name="doc"/>, sorted by kind then name. Nested Revit links are left out.</summary>
        public static List<LiveLink> Read(Document doc)
        {
            Dictionary<ElementId, string> worksets = InstanceWorksets(doc);
            var links = new List<LiveLink>();

            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                if (type.IsNestedLink) continue;
                LinkedFileStatus status;
                try { status = type.GetLinkedFileStatus(); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { status = LinkedFileStatus.Invalid; }

                string path = PathOf(doc, type.Id);
                links.Add(new LiveLink
                {
                    Type = type,
                    Kind = IsIfcPath(path) || IsIfcPath(type.Name) ? LinkKind.Ifc : LinkKind.Revit,
                    Name = type.Name,
                    UniqueId = type.UniqueId,
                    Path = path,
                    Status = status,
                    IsLoaded = status == LinkedFileStatus.Loaded,
                    Workset = worksets.TryGetValue(type.Id, out string ws) ? ws : string.Empty
                });
            }

            foreach (CADLinkType type in new FilteredElementCollector(doc).OfClass(typeof(CADLinkType)).Cast<CADLinkType>())
            {
                bool isLink = type.IsExternalFileReference();
                LinkedFileStatus? status = null;
                if (isLink)
                {
                    try { status = type.GetExternalFileReference()?.GetLinkedFileStatus(); }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { status = LinkedFileStatus.Invalid; }
                }

                links.Add(new LiveLink
                {
                    Type = type,
                    Kind = isLink ? LinkKind.CadLink : LinkKind.CadImport,
                    Name = type.Name,
                    UniqueId = type.UniqueId,
                    Path = isLink ? PathOf(doc, type.Id) : null,
                    Status = status,
                    IsLoaded = !isLink || status == LinkedFileStatus.Loaded,
                    Workset = worksets.TryGetValue(type.Id, out string ws) ? ws : string.Empty
                });
            }

            foreach (PointCloudType type in new FilteredElementCollector(doc).OfClass(typeof(PointCloudType)).Cast<PointCloudType>())
            {
                string path = null;
                try
                {
                    ModelPath mp = type.GetPath();
                    path = mp == null || mp.Empty ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { }

                links.Add(new LiveLink
                {
                    Type = type,
                    Kind = LinkKind.PointCloud,
                    Name = type.Name,
                    UniqueId = type.UniqueId,
                    Path = path,
                    IsLoaded = true,
                    Workset = worksets.TryGetValue(type.Id, out string ws) ? ws : string.Empty
                });
            }

            foreach (ImageType type in new FilteredElementCollector(doc).OfClass(typeof(ImageType)).Cast<ImageType>())
            {
                // Images Revit makes itself (renderings, etc.) are not files.
                if (type.Source == ImageTypeSource.Internal) continue;
                string path = null;
                bool loaded = true;
                try
                {
                    path = type.Path;
                    loaded = type.Status is ImageTypeStatus.Loaded or ImageTypeStatus.Imported or ImageTypeStatus.Generated;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { }

                links.Add(new LiveLink
                {
                    Type = type,
                    Kind = LinkKind.Image,
                    Name = type.Name,
                    UniqueId = type.UniqueId,
                    Path = path,
                    IsLoaded = loaded,
                    Workset = worksets.TryGetValue(type.Id, out string ws) ? ws : string.Empty
                });
            }

            return links.OrderBy(l => l.Kind).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The live link for a saved one: same UniqueId first, then same kind and name (ignoring case).</summary>
        public static LiveLink Match(IEnumerable<LiveLink> live, SavedLink saved)
        {
            var list = live as IList<LiveLink> ?? live.ToList();
            return list.FirstOrDefault(l => l.UniqueId == saved.UniqueId)
                ?? list.FirstOrDefault(l => SameFamily(l.Kind, saved.Kind)
                                            && string.Equals(l.Name, saved.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Display text of <paramref name="kind"/>.</summary>
        public static string KindText(LinkKind kind) => kind switch
        {
            LinkKind.Revit => "Revit",
            LinkKind.Ifc => "IFC",
            LinkKind.CadLink => "CAD link",
            LinkKind.CadImport => "CAD import",
            LinkKind.PointCloud => "Point cloud",
            LinkKind.Image => "Image / PDF",
            _ => kind.ToString()
        };

        // Revit and IFC links are both RevitLinkTypes; CAD links and imports both CADLinkTypes.
        private static bool SameFamily(LinkKind a, LinkKind b)
            => a == b
               || (a is LinkKind.Revit or LinkKind.Ifc && b is LinkKind.Revit or LinkKind.Ifc)
               || (a is LinkKind.CadLink or LinkKind.CadImport && b is LinkKind.CadLink or LinkKind.CadImport);

        // Workset name of the first instance of every link / import type.
        private static Dictionary<ElementId, string> InstanceWorksets(Document doc)
        {
            var result = new Dictionary<ElementId, string>();
            if (!doc.IsWorkshared) return result;

            WorksetTable table = doc.GetWorksetTable();
            var filter = new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementClassFilter(typeof(RevitLinkInstance)),
                new ElementClassFilter(typeof(ImportInstance)),
                new ElementClassFilter(typeof(PointCloudInstance)),
                new ElementClassFilter(typeof(ImageInstance))
            });
            foreach (Element e in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType())
            {
                ElementId typeId = e.GetTypeId();
                if (typeId == ElementId.InvalidElementId || result.ContainsKey(typeId)) continue;
                try { result[typeId] = table.GetWorkset(e.WorksetId)?.Name ?? string.Empty; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { result[typeId] = string.Empty; }
            }
            return result;
        }

        private static string PathOf(Document doc, ElementId typeId)
        {
            try
            {
                ExternalFileReference reference = ExternalFileUtils.GetExternalFileReference(doc, typeId);
                ModelPath path = reference?.GetAbsolutePath();
                return path == null || path.Empty ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;   // cloud links have no file path
            }
        }
    }
}
