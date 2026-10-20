using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.Core.Models;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.Core.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V003.Infrastructure.ExternalEvents
{
    /// <summary>Which Revit-side action the handler runs next.</summary>
    public enum DwgExportRequest
    {
        /// <summary>Collect the DWG instances inside <see cref="DwgExportEventHandler.Scope"/>.</summary>
        Scan,

        /// <summary>Export the DWGs in <see cref="DwgExportEventHandler.ToExport"/>.</summary>
        Export,

        /// <summary>Delete the elements in <see cref="DwgExportEventHandler.ToDelete"/> from the model.</summary>
        DeleteElements
    }

    /// <summary>
    /// Performs all Revit-API work for the Export DWG to Folder tool.
    /// Scan reads the document within the chosen scope; Export writes only the DWGs the user ticked
    /// (isolating each one in its host view first — see <see cref="DwgExportService"/>);
    /// DeleteElements removes the elements the user confirmed deleting after a successful export.
    /// Progress is reported through events raised on Revit's (= the window's) thread.
    /// </summary>
    public class DwgExportEventHandler : IExternalEventHandler
    {
        // ---- Input properties set by the ViewModel before Raise() ----

        /// <summary>The action to run.</summary>
        public DwgExportRequest Request { get; set; }

        /// <summary>Scan scope (Scan only).</summary>
        public DwgScanScope Scope { get; set; }

        /// <summary>Export options (Export only).</summary>
        public ExportDwgSettings Settings { get; set; }

        /// <summary>Root output folder (Export only).</summary>
        public string OutputFolder { get; set; }

        /// <summary>The ticked DWGs to export, in export order (Export only).</summary>
        public List<DwgInstanceInfo> ToExport { get; set; } = new List<DwgInstanceInfo>();

        /// <summary>Elements to delete from the model (DeleteElements only).</summary>
        public List<ElementId> ToDelete { get; set; } = new List<ElementId>();

        // ---- Output properties read by the ViewModel after completion ----

        /// <summary>DWG instances found by the last scan.</summary>
        public List<DwgInstanceInfo> ScannedInstances { get; private set; } = new List<DwgInstanceInfo>();

        /// <summary>DWGs written by the last export.</summary>
        public int ExportedCount { get; private set; }

        /// <summary>DWGs skipped by the last export (file already exists and Overwrite is off).</summary>
        public int SkippedCount { get; private set; }

        /// <summary>DWGs that failed in the last export.</summary>
        public int FailedCount { get; private set; }

        /// <summary>Of the requested elements, how many the last DeleteElements request deleted.</summary>
        public int DeletedCount { get; private set; }

        /// <summary>
        /// Elements removed as a side effect of the last DeleteElements request that were not
        /// themselves in <see cref="ToDelete"/> — e.g. a dimension or tag hosted on a deleted CAD
        /// instance. <c>doc.Delete(id)</c> returns every element it actually removed, which can be
        /// more than the one requested; this is that difference, so the ViewModel can tell the user
        /// "plus N dependent element(s)" instead of silently deleting more than they confirmed.
        /// </summary>
        public int DependentDeletedCount { get; private set; }

        /// <summary>
        /// ElementId.Value of each element in the last DeleteElements request that could not be
        /// deleted (already gone, or Revit refused it).
        /// </summary>
        public List<long> FailedDeleteIds { get; private set; } = new List<long>();

        /// <summary>False when the last request stopped on an unexpected error.</summary>
        public bool LastRunSucceeded { get; private set; }

        /// <summary>Message of the unexpected error, when <see cref="LastRunSucceeded"/> is false.</summary>
        public string ErrorMessage { get; private set; } = string.Empty;

        /// <summary>Scan progress: (stage, current, total).</summary>
        public event Action<string, int, int> ScanProgress;

        /// <summary>Export of item <c>index</c> (0-based) of <c>total</c> is starting.</summary>
        public event Action<int, int> ExportItemStarted;

        /// <summary>Export of item <c>index</c> finished: error is null on success, "SKIP" when skipped, otherwise the failure message.</summary>
        public event Action<int, int, string> ExportItemFinished;

        /// <summary>Raised once the request has finished, successfully or not.</summary>
        public event Action RequestCompleted;

        /// <summary>Revit entry point — guarded so no exception reaches Revit.</summary>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            ErrorMessage = string.Empty;
            LastRunSucceeded = false;

            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null) throw new InvalidOperationException("No active document.");

                switch (Request)
                {
                    case DwgExportRequest.Scan:
                        ExecuteScan(doc);
                        break;
                    case DwgExportRequest.Export:
                        ExecuteExport(doc);
                        break;
                    case DwgExportRequest.DeleteElements:
                        ExecuteDelete(doc);
                        break;
                }

                LastRunSucceeded = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                RequestCompleted?.Invoke();
            }
        }

        /// <summary>Name shown by Revit for this handler.</summary>
        public string GetName() => "Export DWG to Folder";

        // ── Scan ────────────────────────────────────────────────────────────

        private void ExecuteScan(Autodesk.Revit.DB.Document doc)
        {
            ScannedInstances = new List<DwgInstanceInfo>();
            var scope = Scope ?? new DwgScanScope();
            var collector = new DwgCollectorService(doc);
            ScannedInstances = collector.Collect(scope, (stage, current, total) => ScanProgress?.Invoke(stage, current, total));
        }

        // ── Export ──────────────────────────────────────────────────────────

        private void ExecuteExport(Autodesk.Revit.DB.Document doc)
        {
            ExportedCount = 0;
            SkippedCount = 0;
            FailedCount = 0;

            if (string.IsNullOrWhiteSpace(OutputFolder))
                throw new InvalidOperationException("No output folder selected.");

            var exporter = new DwgExportService(doc);
            var settings = Settings ?? new ExportDwgSettings();
            var items = ToExport ?? new List<DwgInstanceInfo>();
            int total = items.Count;

            for (int i = 0; i < total; i++)
            {
                var info = items[i];
                ExportItemStarted?.Invoke(i, total);

                string destPath = BuildDestPath(OutputFolder, info, settings);
                string error = exporter.Export(info, destPath, settings.Overwrite);

                if (error == "SKIP")
                {
                    SkippedCount++;
                }
                else if (error != null)
                {
                    FailedCount++;
                }
                else
                {
                    ExportedCount++;

                    if (settings.GenerateSidecar)
                    {
                        try { exporter.WriteSidecar(info, destPath); }
                        catch { /* sidecar failure doesn't count as export failure */ }
                    }
                }

                ExportItemFinished?.Invoke(i, total, error);
            }
        }

        // ── Delete ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes <see cref="ToDelete"/> from the model in one transaction. Elements that no longer
        /// exist, or that Revit refuses to delete, are recorded in <see cref="FailedDeleteIds"/>
        /// rather than stopping the whole batch — a confirmed-exported CAD instance that can't be
        /// deleted for some reason shouldn't block the rest from being cleaned up.
        /// </summary>
        private void ExecuteDelete(Autodesk.Revit.DB.Document doc)
        {
            DeletedCount = 0;
            DependentDeletedCount = 0;
            FailedDeleteIds = new List<long>();

            var ids = ToDelete ?? new List<ElementId>();
            if (ids.Count == 0) return;

            var requested = new HashSet<long>(ids.Select(i => i.Value));
            var allRemoved = new HashSet<long>();

            using (var tx = new Transaction(doc, "Delete Exported CAD Instances"))
            {
                tx.Start();
                foreach (var id in ids)
                {
                    try
                    {
                        if (doc.GetElement(id) == null)
                        {
                            FailedDeleteIds.Add(id.Value);
                            continue;
                        }

                        // doc.Delete returns every element actually removed, which can include
                        // dependents (a dimension/tag hosted on this CAD instance, etc.), not just
                        // the one id we asked for.
                        foreach (var removedId in doc.Delete(id))
                            allRemoved.Add(removedId.Value);
                        DeletedCount++;
                    }
                    catch
                    {
                        FailedDeleteIds.Add(id.Value);
                    }
                }
                tx.Commit();
            }

            DependentDeletedCount = allRemoved.Count(value => !requested.Contains(value));
        }

        /// <summary>
        /// Builds the full destination path for one DWG.
        /// Structure: OutputFolder \ {source} \ [{viewtype} \] filename.dwg
        /// </summary>
        public static string BuildDestPath(string root, DwgInstanceInfo info, ExportDwgSettings settings)
            => System.IO.Path.Combine(root, BuildRelativeFolder(info, settings.SubfoldersByViewType), info.OutputFileName);

        /// <summary>Relative folder for one DWG: "link\plan", or just "link" when subfolders are off.</summary>
        public static string BuildRelativeFolder(DwgInstanceInfo info, bool subfoldersByViewType)
        {
            string sourceFolder = info.SourceType == DwgSourceType.Link ? "link" : "import";
            if (!subfoldersByViewType)
                return sourceFolder;

            string viewFolder = info.ViewCategory switch
            {
                DwgViewCategory.Plan => "plan",
                DwgViewCategory.Section => "section",
                DwgViewCategory.Drafting => "drafting",
                _ => "no_views"
            };
            return System.IO.Path.Combine(sourceFolder, viewFolder);
        }
    }
}
