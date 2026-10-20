using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    public sealed class ActionResult
    {
        public List<string> Done { get; } = new List<string>();
        public List<string> Failed { get; } = new List<string>();
        public List<string> Skipped { get; } = new List<string>();
        public bool Cancelled { get; set; }

        public string Summary(string verb)
        {
            var lines = new List<string> { $"{verb}: {Done.Count} done, {Failed.Count} failed, {Skipped.Count} skipped" + (Cancelled ? " (cancelled)" : "") };
            if (Done.Count > 0) lines.Add("\nDone:\n  " + string.Join("\n  ", Done));
            if (Failed.Count > 0) lines.Add("\nFailed:\n  " + string.Join("\n  ", Failed));
            if (Skipped.Count > 0) lines.Add("\nSkipped:\n  " + string.Join("\n  ", Skipped));
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Fix actions — single or batch. All run on Revit's main thread (inside the modal window)
    /// and outside any transaction, as Unload / Reload / LoadFrom require.
    /// </summary>
    public sealed class LinkActions
    {
        public const string BackupFolderName = BackupService.FolderName;

        /// <summary>Shown when an In-Model unload hits a CAD link (no API for it).</summary>
        public const string CadUnloadHint =
            "CAD links can't be unloaded by the Revit API in an open model — use Manage Links › CAD Formats › Unload, " +
            "or Pre-Open mode to keep it unloaded next time the host opens";

        private readonly RevitApp _app;
        private readonly Document _activeDoc;     // null in Pre-Open / Folder modes
        private readonly BackupService _backups;

        public LinkActions(RevitApp app, Document activeDocOrNull, BackupService backups)
        {
            _app = app;
            _activeDoc = activeDocOrNull;
            _backups = backups;
        }

        // ------------------------------------------------------------------
        // Upgrade & Save — non-workshared RVTs; each file once
        // ------------------------------------------------------------------
        public async Task<ActionResult> UpgradeAsync(IList<LinkInfo> targets, ScanReporter rep)
        {
            var res = new ActionResult();
            foreach (var t in targets.Where(t => !t.CanUpgrade))
                res.Skipped.Add($"{t.Name} — {(t.IsWorkshared ? "workshared (owner must upgrade)" : "not an older, reachable RVT")}");

            var files = targets.Where(t => t.CanUpgrade)
                               .GroupBy(t => t.FullPath, StringComparer.OrdinalIgnoreCase)
                               .Select(g => g.First()).ToList();

            for (int i = 0; i < files.Count; i++)
            {
                var li = files[i];
                try
                {
                    await rep.StepAsync(i, files.Count, $"Upgrading {i + 1}/{files.Count}: {li.Name}");
                }
                catch (OperationCanceledException)
                {
                    res.Cancelled = true;
                    break;
                }

                try { res.Done.Add(UpgradeOne(li)); }
                catch (Exception ex) { res.Failed.Add($"{li.Name} — {ex.Message}"); }
            }
            rep.Report(files.Count, files.Count, "Upgrade finished");
            return res;
        }

        private string UpgradeOne(LinkInfo li)
        {
            string path = li.FullPath;
            if (_app.Documents.Cast<Document>().Any(d => SamePath(d.PathName, path)))
                throw new InvalidOperationException("file is open in this Revit session");

            // If the active model has it loaded, unload while upgrading, reload after.
            var reload = new List<RevitLinkType>();
            if (_activeDoc != null)
            {
                foreach (var rlt in new FilteredElementCollector(_activeDoc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
                {
                    if (rlt.IsNestedLink || !RevitLinkType.IsLoaded(_activeDoc, rlt.Id)) continue;
                    string p;
                    try { p = ModelPathUtils.ConvertModelPathToUserVisiblePath(rlt.GetExternalFileReference().GetAbsolutePath()); }
                    catch { continue; }
                    if (!SamePath(p, path)) continue;
                    rlt.Unload(null);
                    reload.Add(rlt);
                }
            }

            string backup = _backups.Backup(path, "R" + li.SavedYear);
            string temp = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "__upg_tmp.rvt");
            try
            {
                Document d = _app.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), new OpenOptions { Audit = false });
                try { d.SaveAs(temp, new SaveAsOptions { OverwriteExistingFile = true, Compact = true }); }
                finally { d.Close(false); }

                File.Copy(temp, path, true);
                File.Delete(temp);
                return $"{li.Name} {li.SavedYear} → {_app.VersionNumber} (backup: {backup})";
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
            finally
            {
                foreach (var rlt in reload)
                {
                    try { rlt.Reload(); } catch { /* reload from Manage Links */ }
                }
            }
        }

        // ------------------------------------------------------------------
        // Rebuild IFC caches
        // ------------------------------------------------------------------
        public async Task<ActionResult> RebuildIfcAsync(IList<LinkInfo> targets, ScanReporter rep)
        {
            var res = new ActionResult();
            foreach (var t in targets.Where(t => !t.CanRebuildIfc))
                res.Skipped.Add($"{t.Name} — no IFC cache problem");

            var list = targets.Where(t => t.CanRebuildIfc)
                              .GroupBy(t => t.IfcPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

            for (int i = 0; i < list.Count; i++)
            {
                var li = list[i];
                try { await rep.StepAsync(i, list.Count, $"IFC cache {i + 1}/{list.Count}: {li.Name}"); }
                catch (OperationCanceledException) { res.Cancelled = true; break; }

                if (!li.CacheFolderWritable)
                {
                    res.Failed.Add($"{li.Name} — IFC folder is read-only; copy the IFC to a writable folder and repath");
                    continue;
                }
                try
                {
                    if (File.Exists(li.CachePath))
                    {
                        File.Copy(li.CachePath, li.CachePath + ".bak", true);
                        File.Delete(li.CachePath);
                    }

                    if (_activeDoc != null && li.RefId != null && _activeDoc.GetElement(li.RefId) is RevitLinkType rlt && !rlt.IsNestedLink)
                    {
                        rlt.Reload();
                        res.Done.Add($"{li.Name} — reloaded, new cache written");
                    }
                    else
                    {
                        res.Done.Add($"{li.Name} — old cache moved to .bak; a fresh one is built on the next open of the host");
                    }
                }
                catch (Exception ex) { res.Failed.Add($"{li.Name} — {ex.Message}"); }
            }
            rep.Report(list.Count, list.Count, "IFC caches done");
            return res;
        }

        // ------------------------------------------------------------------
        // Skip load: Pre-Open = stay unloaded at next open (TransmissionData),
        //            In-Model = unload now
        // ------------------------------------------------------------------
        public ActionResult SkipLoad(IList<LinkInfo> targets, bool inModel)
        {
            var res = new ActionResult();
            foreach (var t in targets.Where(t => !t.CanSkipLoad))
                res.Skipped.Add($"{t.Name} — {(t.IsNested ? "nested (controlled by its parent)" : t.IsImport ? "imported, not a link" : "not supported")}");
            var ok = targets.Where(t => t.CanSkipLoad).ToList();

            if (inModel)
            {
                foreach (var li in ok)
                {
                    try
                    {
                        Element e = _activeDoc.GetElement(li.RefId);
                        if (e is RevitLinkType rlt) { rlt.Unload(null); res.Done.Add($"{li.Name} — unloaded"); }
                        // V003: CADLinkType has no Unload() in the Revit API (only Reload / ReloadFrom / LoadFrom),
                        // so CAD links in an open model can't be unloaded from code.
                        else if (e is CADLinkType) res.Skipped.Add($"{li.Name} — {CadUnloadHint}");
                        else res.Skipped.Add($"{li.Name} — unload it from Manage Links");
                    }
                    catch (Exception ex) { res.Failed.Add($"{li.Name} — {ex.Message}"); }
                }
                return res;
            }

            foreach (var host in ok.GroupBy(t => t.HostPath, StringComparer.OrdinalIgnoreCase))
            {
                string block = CanWriteHost(host.Key);
                if (block != null)
                {
                    foreach (var li in host) res.Failed.Add($"{li.Name} — {block}");
                    continue;
                }
                try
                {
                    _backups.Backup(host.Key, "beforeLinkEdit");
                    ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(host.Key);
                    TransmissionData td = TransmissionData.ReadTransmissionData(mp);
                    foreach (var li in host)
                    {
                        ExternalFileReference desired = td.GetDesiredReferenceData(li.RefId);
                        td.SetDesiredReferenceData(li.RefId, desired.GetPath(), desired.PathType, false);
                    }
                    td.IsTransmitted = false;
                    TransmissionData.WriteTransmissionData(mp, td);
                    foreach (var li in host) res.Done.Add($"{li.Name} — stays unloaded next time {li.HostName} opens");
                }
                catch (Exception ex)
                {
                    foreach (var li in host) res.Failed.Add($"{li.Name} — {ex.Message}");
                }
            }
            return res;
        }

        // ------------------------------------------------------------------
        // Repath (single)
        // ------------------------------------------------------------------
        public string Repath(LinkInfo li, string newPath, bool inModel)
        {
            if (li.IsNested) return "Nested links are repathed in their parent file.";

            if (inModel)
            {
                if (!(_activeDoc.GetElement(li.RefId) is RevitLinkType rlt))
                    return "In-Model repath works for RVT links. For CAD/IFC use Manage Links, or Pre-Open mode.";
                LinkLoadResult r = rlt.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(newPath), new WorksetConfiguration());
                return $"Reloaded {li.Name} from {newPath}: {r.LoadResult}";
            }

            string block = CanWriteHost(li.HostPath);
            if (block != null) return block;

            _backups.Backup(li.HostPath, "beforeLinkEdit");
            ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(li.HostPath);
            TransmissionData td = TransmissionData.ReadTransmissionData(mp);
            PathType pt = SameRoot(li.HostPath, newPath) ? PathType.Relative : PathType.Absolute;
            td.SetDesiredReferenceData(li.RefId, ModelPathUtils.ConvertUserVisiblePathToModelPath(newPath), pt, true);
            td.IsTransmitted = false;
            TransmissionData.WriteTransmissionData(mp, td);
            return $"{li.Name} will load from {newPath} ({pt}) next time {li.HostName} opens.";
        }

        // ------------------------------------------------------------------
        public string CanWriteHost(string hostPath)
        {
            if (_app.Documents.Cast<Document>().Any(d => SamePath(d.PathName, hostPath)))
                return "host is open in Revit — close it, or use In-Model mode";
            try
            {
                if (BasicFileInfo.Extract(hostPath).IsCentral)
                    return "host is a workshared CENTRAL — open a local copy and use In-Model mode";
            }
            catch (Exception ex) { return "can't read host: " + ex.Message; }
            return null;
        }

        public static void OpenFolder(LinkInfo li)
        {
            try
            {
                string p = li.Kind == LinkKind.IFC && !string.IsNullOrEmpty(li.IfcPath) ? li.IfcPath : li.FullPath;
                if (File.Exists(p)) Process.Start("explorer.exe", $"/select,\"{p}\"");
                else
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Process.Start("explorer.exe", $"\"{dir}\"");
                }
            }
            catch { /* ignore */ }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private static bool SameRoot(string a, string b)
        {
            try { return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static void TryDelete(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
        }
    }
}
