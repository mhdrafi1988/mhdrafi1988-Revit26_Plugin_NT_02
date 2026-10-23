using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>
    /// Asks ACC whether each loaded cloud link is the current version, through
    /// <c>ExternalResourceReference.GetResourceVersionStatus()</c>.  [V007]
    ///
    /// Runs only when the user presses Check cloud versions: each call may be a network round
    /// trip and needs a signed-in session, so a normal Refresh stays fast and works offline.
    /// Every call is wrapped; a failure sets that row to Unknown with the reason.
    /// </summary>
    public static class CloudVersionChecker
    {
        /// <summary>True for a row this check can answer: a loaded cloud link.</summary>
        public static bool CanCheck(LinkStatusRow row) => row != null && row.IsCloud && row.IsLoaded;

        /// <summary>Checks every checkable row and updates its Latest state. Returns a status-bar line. Never throws.</summary>
        public static string Check(Document doc, IEnumerable<LinkStatusRow> rows)
        {
            List<LinkStatusRow> todo = rows?.Where(CanCheck).ToList() ?? new List<LinkStatusRow>();
            if (todo.Count == 0) return "No loaded cloud links to check.";
            if (doc == null || !doc.IsValidObject) return "The model is no longer open.";

            foreach (LinkStatusRow row in todo) Apply(doc, row);

            int current = todo.Count(r => r.Latest == LatestState.UpToDate);
            int newer = todo.Count(r => r.Latest == LatestState.NewerInAcc);
            int unknown = todo.Count - current - newer;
            return $"Checked {todo.Count} cloud link{(todo.Count == 1 ? "" : "s")} with ACC: {current} current, {newer} newer in ACC, {unknown} unknown.";
        }

        private static void Apply(Document doc, LinkStatusRow row)
        {
            try
            {
                Element e = doc.GetElement(row.TypeId);
                if (e == null) { Set(row, LatestState.Unknown, "The link is no longer in the model. Refresh."); return; }

                IDictionary<ExternalResourceType, ExternalResourceReference> refs = null;
                try { refs = e.GetExternalResourceReferences(); } catch { /* handled below */ }
                if (refs == null || refs.Count == 0)
                {
                    Set(row, LatestState.Unknown, "Revit gives no ACC reference for this link, so ACC cannot be asked.");
                    return;
                }

                foreach (var kv in refs)
                {
                    ExternalResourceReference err = kv.Value;
                    if (err == null) continue;

                    string version = "";
                    try { version = err.Version ?? ""; } catch { /* optional */ }
                    string ver = version.Length > 0 ? $" (version {version})" : "";

                    ResourceVersionStatus status;
                    try { status = err.GetResourceVersionStatus(); }
                    catch (Exception ex) { Set(row, LatestState.Unknown, "ACC could not be asked: " + ex.Message); return; }

                    if (status == ResourceVersionStatus.Current)
                        Set(row, LatestState.UpToDate, $"ACC says the loaded copy is the current version{ver}.");
                    else if (status == ResourceVersionStatus.OutOfDate)
                        Set(row, LatestState.NewerInAcc, $"A newer version is published in ACC than the loaded copy{ver}. Reload the link to get it.");
                    else
                        Set(row, LatestState.Unknown, "ACC did not answer (offline, signed out, or no access to that project).");
                    return;                                  // one answer per link type
                }

                Set(row, LatestState.Unknown, "No usable ACC reference on this link.");
            }
            catch (Exception ex)
            {
                Set(row, LatestState.Unknown, "Check failed: " + ex.Message);
            }
        }

        private static void Set(LinkStatusRow row, LatestState state, string reason)
        {
            row.Latest = state;
            row.LatestReason = reason;
        }
    }
}
