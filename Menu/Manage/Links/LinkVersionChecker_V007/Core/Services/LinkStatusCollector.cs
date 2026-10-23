using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>What one scan of the open model found.</summary>
    public sealed class LinkStatusScan
    {
        /// <summary>Every top-level Revit link: Desktop Connector, cloud, and other paths (the grid hides those by default).</summary>
        public List<LinkStatusRow> Rows { get; } = new();

        /// <summary>Top-level Revit links that are neither Desktop Connector nor cloud links.</summary>
        public int OtherLinks => Rows.Count(r => r.Source == LinkSourceKind.Other);

        /// <summary>The Desktop Connector project folder of the links (most common one), or empty.</summary>
        public string ProjectRoot { get; set; } = "";

        /// <summary>ACC project of the open model, or empty when the model is not in ACC.  [V007]</summary>
        public string HostProject { get; set; } = "";

        /// <summary>ACC account of the open model, or empty.  [V007]</summary>
        public string HostAccount { get; set; } = "";

        /// <summary>Links that could not be read, with the reason; shown in the status bar.</summary>
        public List<string> Skipped { get; } = new();
    }

    /// <summary>
    /// Collects the status of every top-level Revit link in a model and sorts it into
    /// Desktop Connector, cloud link or other path. Read-only: makes no change to the model.
    /// </summary>
    public static class LinkStatusCollector
    {
        /// <summary>What a link type's saved reference resolved to.</summary>
        private sealed class LinkRef
        {
            public string Path = "";
            public bool Cloud;
            public string ProjectId = "";
            public string ModelId = "";
        }

        /// <summary>Scans <paramref name="doc"/>. Never throws for one bad link; skips it with a reason.</summary>
        public static LinkStatusScan Scan(Document doc)
        {
            var scan = new LinkStatusScan();
            if (doc == null) return scan;

            // Instances per type, and one loaded link document per type for the version compare.
            var instanceCount = new Dictionary<ElementId, int>();
            var linkDocs = new Dictionary<ElementId, Document>();
            foreach (RevitLinkInstance inst in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
            {
                ElementId typeId = inst.GetTypeId();
                instanceCount[typeId] = instanceCount.TryGetValue(typeId, out int n) ? n + 1 : 1;
                if (!linkDocs.ContainsKey(typeId))
                {
                    Document ld = null;
                    try { ld = inst.GetLinkDocument(); } catch { /* not loaded */ }
                    if (ld != null) linkDocs[typeId] = ld;
                }
            }

            // Pass 1: resolve every link's reference, so project names seen in Desktop Connector
            // paths can help read cloud paths that were saved without the account name.
            var refs = new List<(RevitLinkType Type, LinkRef Ref)>();
            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)))
            {
                try
                {
                    if (type.IsNestedLink) continue;
                    refs.Add((type, Resolve(type)));
                }
                catch (Exception ex)
                {
                    scan.Skipped.Add($"{SafeName(type)}: {ex.Message}");
                }
            }

            (string hostId, AccPathParts host) = HostProject(doc);
            var knownProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (host.Project.Length > 0) knownProjects.Add(host.Project);
            foreach (var (_, r) in refs)
                if (!r.Cloud && DesktopConnectorProbe.IsDesktopConnectorPath(r.Path))
                {
                    string p = AccPath.ParseDesktopConnector(r.Path).Project;
                    if (p.Length > 0) knownProjects.Add(p);
                }
            scan.HostProject = host.Project;
            scan.HostAccount = host.Account;

            var roots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var (type, lr) in refs)
            {
                try
                {
                    ModelLoadState model = MapStatus(type);
                    int instances = instanceCount.TryGetValue(type.Id, out int k) ? k : 0;
                    string owner = OwnerIfOther(doc, type.Id);

                    if (lr.Cloud)
                    {
                        AccPathParts parts = AccPath.ParseCloud(lr.Path, knownProjects.Contains);
                        scan.Rows.Add(new LinkStatusRow
                        {
                            TypeId = type.Id,
                            Name = parts.FileName.Length > 0 ? parts.FileName : SafeName(type),
                            FullPath = lr.Path,
                            Source = LinkSourceKind.Cloud,
                            AccAccount = parts.Account,
                            AccProject = parts.Project,
                            SameProject = SameProject(hostId, host.Project, lr.ProjectId, parts.Project),
                            CloudProjectId = lr.ProjectId,
                            CloudModelId = lr.ModelId,
                            AccFolder = parts.Folder,
                            Model = model,
                            Dc = DcFileState.CloudModel,
                            Latest = model == ModelLoadState.Loaded ? LatestState.NotChecked : LatestState.NotLoaded,
                            LatestReason = model == ModelLoadState.Loaded
                                ? "Press Check cloud versions to ask ACC whether this is the current version."
                                : "The link is not loaded, so there is no loaded copy to compare.",
                            Instances = instances,
                            OwnedBy = owner
                        });
                        continue;
                    }

                    string path = lr.Path;
                    if (!DesktopConnectorProbe.IsDesktopConnectorPath(path))
                    {
                        // Not ACC: listed, but hidden unless "Show other links" is on. The file is not
                        // touched, because an offline network share can stall for seconds per file.
                        scan.Rows.Add(new LinkStatusRow
                        {
                            TypeId = type.Id,
                            Name = path.Length > 0 ? Path.GetFileName(path) : SafeName(type),
                            FullPath = path,
                            Source = LinkSourceKind.Other,
                            AccFolder = SafeDirectory(path),
                            Model = model,
                            Dc = DcFileState.NotAcc,
                            Latest = LatestState.NotApplicable,
                            LatestReason = "Not an ACC link, so there is no ACC version to compare with.",
                            Instances = instances,
                            OwnedBy = owner
                        });
                        continue;
                    }

                    string root = DesktopConnectorProbe.ProjectRoot(path);
                    roots[root] = roots.TryGetValue(root, out int c) ? c + 1 : 1;

                    DcFileState dc = DesktopConnectorProbe.GetState(path);

                    DateTime? date = null;
                    double? size = null;
                    var fi = new FileInfo(path);
                    if (fi.Exists) { date = fi.LastWriteTime; size = fi.Length / 1048576.0; }

                    linkDocs.TryGetValue(type.Id, out Document linkDoc);
                    (LatestState latest, string reason) = CompareLatest(model, dc, path, linkDoc);
                    AccPathParts dcParts = AccPath.ParseDesktopConnector(path);

                    scan.Rows.Add(new LinkStatusRow
                    {
                        TypeId = type.Id,
                        Name = Path.GetFileName(path),
                        FullPath = path,
                        Source = LinkSourceKind.DesktopConnector,
                        AccAccount = dcParts.Account,
                        AccProject = dcParts.Project,
                        SameProject = SameProject(hostId, host.Project, "", dcParts.Project),
                        AccFolder = DesktopConnectorProbe.FolderInProject(path),
                        Model = model,
                        Dc = dc,
                        Latest = latest,
                        LatestReason = reason,
                        FileDate = date,
                        SizeMb = size,
                        Instances = instances,
                        OwnedBy = owner
                    });
                }
                catch (Exception ex)
                {
                    scan.Skipped.Add($"{SafeName(type)}: {ex.Message}");
                }
            }

            scan.ProjectRoot = roots.OrderByDescending(r => r.Value).Select(r => r.Key).FirstOrDefault() ?? "";
            scan.Rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return scan;
        }

        /// <summary>
        /// Reads a link type's saved reference. A file reference gives a path (its <c>ModelPath</c>
        /// may itself be a cloud path); a cloud link that is not a file reference is read through
        /// its external resource reference. Never throws.
        /// </summary>
        private static LinkRef Resolve(RevitLinkType type)
        {
            var r = new LinkRef();

            ExternalFileReference efr = null;
            try { if (type.IsExternalFileReference()) efr = type.GetExternalFileReference(); } catch { /* cloud link */ }
            if (efr != null)
            {
                ModelPath mp = null;
                try { mp = efr.GetAbsolutePath(); } catch { /* fall back to the saved path */ }
                if (mp == null) { try { mp = efr.GetPath(); } catch { /* no path */ } }
                r.Path = UserPath(mp);

                bool cloud = false;
                try { cloud = mp != null && mp.CloudPath; } catch { /* not a cloud path */ }
                if (cloud || AccPath.IsCloudPath(r.Path))
                {
                    r.Cloud = true;
                    try { r.ProjectId = mp.GetProjectGUID().ToString(); } catch { /* optional */ }
                    try { r.ModelId = mp.GetModelGUID().ToString(); } catch { /* optional */ }
                }
                return r;
            }

            IDictionary<ExternalResourceType, ExternalResourceReference> res = null;
            try { res = type.GetExternalResourceReferences(); } catch { /* none */ }
            if (res == null) return r;

            foreach (var kv in res)
            {
                ExternalResourceReference err = kv.Value;
                if (err == null) continue;
                try { r.Path = err.InSessionPath ?? ""; } catch { /* optional */ }
                IDictionary<string, string> info = null;
                try { info = err.GetReferenceInformation(); } catch { /* optional */ }
                r.ProjectId = AccPath.Find(info, "project");
                r.ModelId = AccPath.Find(info, "model");
                r.Cloud = AccPath.IsCloudPath(r.Path) || r.ProjectId.Length > 0;
                break;                                   // one reference per link type
            }
            return r;
        }

        /// <summary>The open model's ACC project id and names: from its cloud path, or from its (central) Desktop Connector path.</summary>
        private static (string Id, AccPathParts Parts) HostProject(Document doc)
        {
            try
            {
                if (doc.IsModelInCloud)
                {
                    ModelPath mp = doc.GetCloudModelPath();
                    string id = "";
                    try { id = mp.GetProjectGUID().ToString(); } catch { /* optional */ }
                    return (id, AccPath.ParseCloud(UserPath(mp)));
                }

                string path = doc.PathName ?? "";
                if (doc.IsWorkshared)
                {
                    try
                    {
                        string central = UserPath(doc.GetWorksharingCentralModelPath());
                        if (central.Length > 0) path = central;
                    }
                    catch { /* detached: use the file itself */ }
                }
                if (AccPath.IsCloudPath(path)) return ("", AccPath.ParseCloud(path));
                return ("", AccPath.ParseDesktopConnector(path));
            }
            catch
            {
                return ("", new AccPathParts());
            }
        }

        /// <summary>Same ACC project as the open model: by project id when both have one, else by project name; null when unknown.</summary>
        private static bool? SameProject(string hostId, string hostName, string linkId, string linkName)
        {
            if (!string.IsNullOrEmpty(hostId) && !string.IsNullOrEmpty(linkId)) return AccPath.SameId(hostId, linkId);
            if (!string.IsNullOrEmpty(hostName) && !string.IsNullOrEmpty(linkName))
                return string.Equals(hostName, linkName, StringComparison.OrdinalIgnoreCase);
            return null;
        }

        private static string UserPath(ModelPath mp)
        {
            try { return mp == null || mp.Empty ? "" : ModelPathUtils.ConvertModelPathToUserVisiblePath(mp) ?? ""; }
            catch { return ""; }
        }

        private static string SafeName(RevitLinkType type)
        {
            try { return type?.Name ?? "(unnamed link)"; } catch { return "(unnamed link)"; }
        }

        private static string SafeDirectory(string path)
        {
            try { return Path.GetDirectoryName(path) ?? ""; } catch { return ""; }
        }

        private static ModelLoadState MapStatus(RevitLinkType type)
        {
            LinkedFileStatus s;
            try { s = type.GetLinkedFileStatus(); }
            catch { return ModelLoadState.Other; }

            return s switch
            {
                LinkedFileStatus.Loaded => ModelLoadState.Loaded,
                LinkedFileStatus.Unloaded => ModelLoadState.Unloaded,
                LinkedFileStatus.LocallyUnloaded => ModelLoadState.UnloadedForMe,
                LinkedFileStatus.InClosedWorkset => ModelLoadState.InClosedWorkset,
                LinkedFileStatus.NotFound => ModelLoadState.NotFound,
                _ => ModelLoadState.Other
            };
        }

        /// <summary>
        /// Compares the copy loaded in the model with the file on this PC. Both carry a document
        /// version (save GUID + number of saves); different versions mean a newer save has
        /// arrived since the link was loaded. Online-only files are not read, because reading
        /// the header would make Desktop Connector download the whole file.
        /// </summary>
        private static (LatestState, string) CompareLatest(ModelLoadState model, DcFileState dc, string path, Document linkDoc)
        {
            if (model != ModelLoadState.Loaded) return (LatestState.NotLoaded, "The link is not loaded, so there is no loaded copy to compare.");
            if (dc == DcFileState.SyncPending) return (LatestState.SyncPending, "Desktop Connector is still syncing this file; check again when it finishes.");
            if (dc == DcFileState.OnlineOnly) return (LatestState.Unknown, "The file is online only; reading it would download it. Use \"Always keep on this device\" first.");
            if (dc == DcFileState.Missing) return (LatestState.Unknown, "There is no file at this path on this PC.");
            if (linkDoc == null) return (LatestState.Unknown, "Revit did not return the loaded link document.");

            try
            {
                DocumentVersion loaded = Document.GetDocumentVersion(linkDoc);
                DocumentVersion onDisk = BasicFileInfo.Extract(path)?.GetDocumentVersion();
                if (loaded == null || onDisk == null) return (LatestState.Unknown, "The file does not carry a version stamp.");

                if (loaded.IsEqual(onDisk))
                {
                    return dc is DcFileState.AlwaysKept or DcFileState.AvailableLocally
                        ? (LatestState.UpToDate, $"The loaded copy is the file Desktop Connector has (save {onDisk.NumberOfSaves}).")
                        : (LatestState.Unknown, $"The loaded copy matches the file on this PC (save {onDisk.NumberOfSaves}), but Windows does not report it as synced with ACC.");
                }

                return (LatestState.ReloadNeeded,
                    $"The file on this PC is a different save (save {onDisk.NumberOfSaves}) from the copy loaded in the model (save {loaded.NumberOfSaves}). Reload the link.");
            }
            catch (Exception ex)
            {
                return (LatestState.Unknown, "Could not read the file's version: " + ex.Message);
            }
        }

        /// <summary>The other user who owns the element, or empty.</summary>
        internal static string OwnerIfOther(Document doc, ElementId id)
        {
            try
            {
                if (!doc.IsWorkshared) return "";
                CheckoutStatus st = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
                return st == CheckoutStatus.OwnedByOtherUser ? (owner ?? "another user") : "";
            }
            catch { return ""; }
        }
    }
}
