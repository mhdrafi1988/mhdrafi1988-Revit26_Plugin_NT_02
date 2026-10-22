using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Services
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
                    ? $"{row.Name}: reloaded." + (row.Dc == DcFileState.OnlineOnly ? " (Desktop Connector downloaded it first.)" : "")
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

        /// <summary>Marks the file "Always keep on this device".</summary>
        public static string AlwaysKeep(LinkStatusRow row)
        {
            if (row == null) return "Select a link first.";
            if (row.Dc == DcFileState.AlwaysKept) return $"{row.Name} is already kept on this device.";
            string err = DesktopConnectorProbe.Pin(row.FullPath);
            return err == null
                ? $"{row.Name}: set to always keep on this device. Desktop Connector downloads it in the background — Refresh to see progress."
                : $"{row.Name}: could not set always keep — {err}";
        }

        /// <summary>Opens Explorer with the file selected (or its folder when the file is missing).</summary>
        public static string OpenFolder(LinkStatusRow row)
        {
            if (row == null) return "Select a link first.";
            try
            {
                if (File.Exists(row.FullPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.FullPath}\"") { UseShellExecute = true });
                    return $"Opened the folder of {row.Name}.";
                }

                string dir = Path.GetDirectoryName(row.FullPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                    return $"{row.Name} is not in its folder; opened the folder.";
                }
                return $"The folder of {row.Name} does not exist on this PC.";
            }
            catch (Exception ex)
            {
                return $"Could not open the folder — {ex.Message}";
            }
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
