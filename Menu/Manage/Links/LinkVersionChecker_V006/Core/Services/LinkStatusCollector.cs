using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Services
{
    /// <summary>What one scan of the open model found.</summary>
    public sealed class LinkStatusScan
    {
        /// <summary>Top-level Revit links stored under the Desktop Connector folder.</summary>
        public List<LinkStatusRow> Rows { get; } = new();

        /// <summary>Top-level Revit links that do not come from ACC (hidden from the grid).</summary>
        public int OtherLinks { get; set; }

        /// <summary>The Desktop Connector project folder of the links (most common one), or empty.</summary>
        public string ProjectRoot { get; set; } = "";

        /// <summary>Links that could not be read, with the reason; shown in the status bar.</summary>
        public List<string> Skipped { get; } = new();
    }

    /// <summary>
    /// Collects the status of every top-level Revit link in a model that comes from
    /// Desktop Connector. Read-only: makes no change to the model.
    /// </summary>
    public static class LinkStatusCollector
    {
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

            var roots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string hostProjectId = AccCloudProbe.HostProjectId(doc);

            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)))
            {
                string name = type.Name ?? "(unnamed link)";
                try
                {
                    if (type.IsNestedLink) continue;

                    string path = LinkPath(doc, type);
                    if (!DesktopConnectorProbe.IsDesktopConnectorPath(path))
                    {
                        // Not a Desktop Connector file: an ACC cloud link has no file path at all.
                        CloudLinkInfo cloud = path.Length == 0 ? AccCloudProbe.Read(doc, type, hostProjectId) : null;
                        if (cloud == null) { scan.OtherLinks++; continue; }

                        ModelLoadState cmodel = MapStatus(type);
                        scan.Rows.Add(new LinkStatusRow
                        {
                            TypeId = type.Id,
                            Name = cloud.FileName,
                            FullPath = cloud.InSessionPath,
                            AccFolder = cloud.Folder,
                            Origin = cloud.Origin,
                            Model = cmodel,
                            Dc = DcFileState.CloudOnly,
                            Latest = cmodel == ModelLoadState.Loaded ? LatestState.NotChecked : LatestState.NotLoaded,
                            LatestReason = cmodel == ModelLoadState.Loaded
                                ? "Press Check ACC status to ask ACC whether the loaded copy is the current version."
                                : "The link is not loaded, so there is no loaded copy to compare.",
                            Instances = instanceCount.TryGetValue(type.Id, out int ck) ? ck : 0,
                            OwnedBy = OwnerIfOther(doc, type.Id)
                        });
                        continue;
                    }

                    string root = DesktopConnectorProbe.ProjectRoot(path);
                    roots[root] = roots.TryGetValue(root, out int c) ? c + 1 : 1;

                    ModelLoadState model = MapStatus(type);
                    DcFileState dc = DesktopConnectorProbe.GetState(path);

                    DateTime? date = null;
                    double? size = null;
                    var fi = new FileInfo(path);
                    if (fi.Exists) { date = fi.LastWriteTime; size = fi.Length / 1048576.0; }

                    linkDocs.TryGetValue(type.Id, out Document linkDoc);
                    (LatestState latest, string reason) = CompareLatest(model, dc, path, linkDoc);

                    scan.Rows.Add(new LinkStatusRow
                    {
                        TypeId = type.Id,
                        Name = Path.GetFileName(path),
                        FullPath = path,
                        AccFolder = DesktopConnectorProbe.FolderInProject(path),
                        Model = model,
                        Dc = dc,
                        Latest = latest,
                        LatestReason = reason,
                        FileDate = date,
                        SizeMb = size,
                        Instances = instanceCount.TryGetValue(type.Id, out int k) ? k : 0,
                        OwnedBy = OwnerIfOther(doc, type.Id)
                    });
                }
                catch (Exception ex)
                {
                    scan.Skipped.Add($"{name}: {ex.Message}");
                }
            }

            scan.ProjectRoot = roots.OrderByDescending(r => r.Value).Select(r => r.Key).FirstOrDefault() ?? "";
            scan.Rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return scan;
        }

        /// <summary>The link's saved absolute path as the user sees it, or empty for cloud / server links.</summary>
        private static string LinkPath(Document doc, RevitLinkType type)
        {
            ExternalFileReference efr = null;
            try { efr = type.GetExternalFileReference(); } catch { /* cloud or server link */ }
            if (efr == null) return "";
            ModelPath mp = efr.GetAbsolutePath();
            return mp == null ? "" : ModelPathUtils.ConvertModelPathToUserVisiblePath(mp) ?? "";
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
