using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>
    /// The actions the window offers on the selected link. Each returns a message for the
    /// status bar and never throws. Reload / Unload must run in Revit's API context with no
    /// transaction open (Revit manages its own); the modal window guarantees that.
    /// </summary>
    public static class LinkStatusActions
    {
        /// <summary>Reloads the link (or restores a link unloaded only for this user).</summary>
        public static string Reload(Document doc, LinkStatusRow row)
        {
            if (!TryGetType(doc, row, out RevitLinkType type, out string error)) return error;
            try
            {
                if (type.LocallyUnloaded)
                {
                    LinkedFileStatus s = type.RevertLocalUnloadStatus();
                    return $"{row.Name}: unload-for-me removed (status now {s}).";
                }

                string owner = LinkStatusCollector.OwnerIfOther(doc, type.Id);
                if (owner.Length > 0) return $"{row.Name} is being edited by {owner}. Ask them to sync and relinquish it, then try again.";

                LinkLoadResult r = type.Reload();
                return r.LoadResult == LinkLoadResultType.LinkLoaded
                    ? $"{row.Name}: reloaded." + (row.Dc == DcFileState.OnlineOnly ? " (Desktop Connector downloaded it first.)" :
                                                   row.IsCloud ? " Press Check cloud versions to confirm it is current." : "")
                    : $"{row.Name}: Revit could not reload it ({r.LoadResult}).";
            }
            catch (Exception ex)
            {
                return $"{row.Name}: reload failed — {ex.Message}";
            }
        }

        /// <summary>
        /// Unloads the link. In a workshared local copy this unloads it for the current user
        /// only, so other people's models are not affected; otherwise for everyone.
        /// </summary>
        public static string Unload(Document doc, LinkStatusRow row)
        {
            if (!TryGetType(doc, row, out RevitLinkType type, out string error)) return error;
            try
            {
                // Workshared local copy: unload for this user only, so nobody else is affected.
                // Revit refuses UnloadLocally in a central model; fall through to a full unload there.
                if (doc.IsWorkshared && !IsCentral(doc))
                {
                    if (type.LocallyUnloaded) return $"{row.Name} is already unloaded for you.";
                    bool ok = type.UnloadLocally(null);
                    return ok ? $"{row.Name}: unloaded for you only (other users are not affected)." : $"{row.Name}: Revit did not unload it.";
                }

                string owner = LinkStatusCollector.OwnerIfOther(doc, type.Id);
                if (owner.Length > 0) return $"{row.Name} is being edited by {owner}. Ask them to sync and relinquish it, then try again.";

                type.Unload(null);
                return $"{row.Name}: unloaded.";
            }
            catch (Exception ex)
            {
                return $"{row.Name}: unload failed — {ex.Message}";
            }
        }

        /// <summary>
        /// Marks every Desktop Connector file in <paramref name="rows"/> "Always keep on this device".
        /// Files already kept or not on this PC, cloud links and other links are skipped.
        /// Returns one status-bar line.
        /// </summary>
        public static string AlwaysKeep(IReadOnlyList<LinkStatusRow> rows)
        {
            if (rows == null || rows.Count == 0) return "Select a link first.";

            int done = 0, skipped = 0;
            var failed = new List<string>();
            foreach (LinkStatusRow row in rows)
            {
                if (!row.IsDesktopConnector || row.Dc is DcFileState.AlwaysKept or DcFileState.Missing) { skipped++; continue; }
                string err = DesktopConnectorProbe.Pin(row.FullPath);
                if (err == null) done++;
                else failed.Add($"{row.Name} ({err})");
            }

            string msg = done == 1 && rows.Count == 1
                ? $"{rows[0].Name}: set to always keep on this device."
                : $"{done} file(s) set to always keep on this device.";
            if (skipped > 0) msg += $" {skipped} skipped (already kept, not on this PC, or not a Desktop Connector file).";
            if (failed.Count > 0) msg += $" Failed: {string.Join("; ", failed.Take(3))}{(failed.Count > 3 ? " …" : "")}.";
            if (done > 0) msg += " Desktop Connector downloads them in the background — Refresh to see progress.";
            return msg;
        }

        /// <summary>Distinct folders of <paramref name="rows"/>, so the caller can confirm before opening many windows.</summary>
        public static int FolderCount(IEnumerable<LinkStatusRow> rows) =>
            rows?.Where(r => !r.IsCloud)
                 .Select(r => SafeDirectory(r.FullPath))
                 .Where(d => d.Length > 0)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .Count() ?? 0;

        /// <summary>
        /// Opens one Explorer window per distinct folder of <paramref name="rows"/>, with the
        /// selected files in that folder highlighted. A folder whose files are all missing opens
        /// without a selection; a folder that does not exist is reported. Cloud links have no
        /// folder on this PC and are skipped.
        /// </summary>
        public static string OpenFolders(IReadOnlyList<LinkStatusRow> rows)
        {
            if (rows == null || rows.Count == 0) return "Select a link first.";

            int opened = 0, cloud = rows.Count(r => r.IsCloud);
            var missing = new List<string>();
            foreach (var group in rows.Where(r => !r.IsCloud).GroupBy(r => SafeDirectory(r.FullPath), StringComparer.OrdinalIgnoreCase))
            {
                string dir = group.Key;
                if (dir.Length == 0 || !Directory.Exists(dir)) { missing.Add(dir.Length == 0 ? group.First().Name : dir); continue; }

                var files = group.Select(r => r.FullPath).Where(File.Exists).ToList();
                try
                {
                    if (files.Count == 0)
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                    else
                        ExplorerSelect.Open(dir, files);
                    opened++;
                }
                catch (Exception ex)
                {
                    missing.Add($"{dir} ({ex.Message})");
                }
            }

            string msg = $"Opened {opened} folder{(opened == 1 ? "" : "s")}.";
            if (missing.Count > 0) msg += $" Not opened: {string.Join("; ", missing.Take(3))}{(missing.Count > 3 ? " …" : "")}.";
            if (cloud > 0) msg += $" {cloud} cloud link{(cloud == 1 ? "" : "s")} skipped (no folder on this PC).";
            return msg;
        }

        /// <summary>Copies the full path of every row to the clipboard, one per line.</summary>
        public static string CopyPaths(IReadOnlyList<LinkStatusRow> rows)
        {
            if (rows == null || rows.Count == 0) return "Select a link first.";
            try
            {
                System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(r => r.FullPath)));
                return rows.Count == 1 ? $"Copied the path of {rows[0].Name}." : $"Copied {rows.Count} paths.";
            }
            catch (Exception ex)
            {
                return "Could not copy to the clipboard — " + ex.Message;
            }
        }

        private static string SafeDirectory(string path)
        {
            try { return Path.GetDirectoryName(path) ?? ""; } catch { return ""; }
        }

        private static bool TryGetType(Document doc, LinkStatusRow row, out RevitLinkType type, out string error)
        {
            type = null;
            error = null;
            if (row == null) { error = "Select a link first."; return false; }
            if (doc == null || !doc.IsValidObject) { error = "The model is no longer open."; return false; }
            if (doc.IsReadOnly) { error = "The model is read-only."; return false; }
            type = doc.GetElement(row.TypeId) as RevitLinkType;
            if (type == null) { error = $"{row.Name} is no longer in the model. Refresh."; return false; }
            return true;
        }

        private static bool IsCentral(Document doc)
        {
            try
            {
                if (doc.IsModelInCloud) return false;   // a cloud model open in Revit is always a local copy
                ModelPath central = doc.GetWorksharingCentralModelPath();
                if (central == null) return false;
                string c = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                return string.Equals(c, doc.PathName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
