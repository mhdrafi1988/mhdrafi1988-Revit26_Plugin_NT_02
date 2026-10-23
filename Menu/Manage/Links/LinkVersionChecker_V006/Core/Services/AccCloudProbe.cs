using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Services
{
    /// <summary>What Revit says about one ACC / BIM 360 cloud link.</summary>
    public sealed class CloudLinkInfo
    {
        /// <summary>The path as Revit shows it, e.g. <c>Autodesk Docs://Tower-A/02_ARC/Models/ARC_Core.rvt</c>.</summary>
        public string InSessionPath { get; init; } = "";

        /// <summary>File name taken from the path.</summary>
        public string FileName { get; init; } = "";

        /// <summary>Folders between the project and the file, joined with backslashes; "(project root)" when none.</summary>
        public string Folder { get; init; } = "";

        /// <summary>Where the link comes from (this project, another project, legacy BIM 360).</summary>
        public LinkOrigin Origin { get; init; }
    }

    /// <summary>
    /// Reads ACC / BIM 360 cloud links. A cloud link has no file path: Revit exposes it as an
    /// <see cref="ExternalResourceReference"/> (see <c>Element.GetExternalResourceReferences</c>).
    /// Reading the reference is local; asking ACC whether the loaded copy is current
    /// (<see cref="CheckLatest"/>) needs the network and a sign-in, so it only runs on demand.
    /// Every method here is safe on any element and never throws.
    /// </summary>
    public static class AccCloudProbe
    {
        /// <summary>
        /// Reads the cloud reference of <paramref name="type"/>. Returns null when the link is not an
        /// Autodesk cloud link (a file link, a Revit Server link, another resource server).
        /// </summary>
        public static CloudLinkInfo Read(Document doc, RevitLinkType type, string hostProjectId)
        {
            try
            {
                if (type.IsExternalFileReference()) return null;

                IDictionary<ExternalResourceType, ExternalResourceReference> refs = null;
                try { refs = type.GetExternalResourceReferences(); } catch { /* none */ }
                if (refs == null) return null;

                foreach (ExternalResourceReference err in refs.Values)
                {
                    if (err == null) continue;

                    string path = "";
                    try { path = err.InSessionPath ?? ""; } catch { /* ignore */ }

                    IDictionary<string, string> info = null;
                    try { info = err.GetReferenceInformation(); } catch { /* ignore */ }
                    string projectId = info == null ? "" : Find(info, "project");

                    bool bim360 = path.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase);
                    bool autodesk = bim360 || projectId.Length > 0 ||
                                    path.StartsWith("Autodesk Docs", StringComparison.OrdinalIgnoreCase) ||
                                    path.StartsWith("ACC", StringComparison.OrdinalIgnoreCase);
                    if (!autodesk) continue;

                    LinkOrigin origin = bim360 ? LinkOrigin.Bim360
                        : hostProjectId.Length > 0 && projectId.Length > 0 &&
                          !string.Equals(Normalise(projectId), Normalise(hostProjectId), StringComparison.OrdinalIgnoreCase)
                            ? LinkOrigin.AccCloudOtherProject
                            : LinkOrigin.AccCloud;

                    SplitPath(path, out string name, out string folder);
                    return new CloudLinkInfo
                    {
                        InSessionPath = path,
                        FileName = name.Length > 0 ? name : type.Name,
                        Folder = folder,
                        Origin = origin
                    };
                }
            }
            catch
            {
                // treat as "not a cloud link"
            }
            return null;
        }

        /// <summary>The cloud project id of the open model, or empty when the model is not a cloud model.</summary>
        public static string HostProjectId(Document doc)
        {
            try
            {
                if (doc == null || !doc.IsModelInCloud) return "";
                ModelPath mp = doc.GetCloudModelPath();
                return mp == null ? "" : mp.GetProjectGUID().ToString();
            }
            catch { return ""; }
        }

        /// <summary>
        /// Splits <c>Autodesk Docs://Project/Folder/Sub/File.rvt</c> into the file name and
        /// <c>Folder\Sub</c> (the project name is dropped, like "Project Files" is for disk paths).
        /// </summary>
        internal static void SplitPath(string path, out string fileName, out string folder)
        {
            fileName = "";
            folder = "";
            if (string.IsNullOrEmpty(path)) return;

            string p = path;
            int scheme = p.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) p = p.Substring(scheme + 3);

            string[] parts = p.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            fileName = parts[parts.Length - 1];
            // parts[0] is the project name; what is between it and the file is the folder.
            folder = parts.Length > 2
                ? string.Join("\\", parts, 1, parts.Length - 2)
                : "(project root)";
        }

        /// <summary>
        /// Asks ACC whether the loaded copy of <paramref name="row"/> is the current version
        /// (<c>ExternalResourceReference.GetResourceVersionStatus</c>). Needs a signed-in session and
        /// the network. Sets <see cref="LinkStatusRow.Latest"/> and its reason; never throws.
        /// </summary>
        public static void CheckLatest(Document doc, LinkStatusRow row)
        {
            if (row == null || !row.IsCloud) return;
            if (!row.IsLoaded) { Set(row, LatestState.NotLoaded, "The link is not loaded, so there is no loaded copy to compare."); return; }

            try
            {
                Element e = doc?.GetElement(row.TypeId);
                if (e == null) { Set(row, LatestState.Unknown, "The link is no longer in the model."); return; }

                IDictionary<ExternalResourceType, ExternalResourceReference> refs = e.GetExternalResourceReferences();
                ExternalResourceReference err = refs?.Values.FirstOrDefault(r => r != null);
                if (err == null) { Set(row, LatestState.Unknown, "Revit exposes no cloud reference for this link."); return; }

                ResourceVersionStatus status = err.GetResourceVersionStatus();
                switch (status)
                {
                    case ResourceVersionStatus.Current:
                        Set(row, LatestState.UpToDate, "ACC says the loaded copy is the current version.");
                        break;
                    case ResourceVersionStatus.OutOfDate:
                        Set(row, LatestState.ReloadNeeded, "ACC has a newer version than the loaded copy. Reload the link.");
                        break;
                    default:
                        Set(row, LatestState.Unknown, "ACC could not say (offline, signed out, or a legacy path).");
                        break;
                }
            }
            catch (Exception ex)
            {
                Set(row, LatestState.Unknown, "The ACC check failed: " + ex.Message);
            }
        }

        private static void Set(LinkStatusRow row, LatestState state, string reason)
        {
            row.Latest = state;
            row.LatestReason = reason;
        }

        private static string Find(IDictionary<string, string> info, string keyPart)
        {
            foreach (var kv in info)
                if (kv.Key != null && kv.Key.IndexOf(keyPart, StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(kv.Value))
                    return kv.Value;
            return "";
        }

        private static string Normalise(string id) => (id ?? "").Trim().Trim('{', '}').Replace("b.", "");
    }
}
