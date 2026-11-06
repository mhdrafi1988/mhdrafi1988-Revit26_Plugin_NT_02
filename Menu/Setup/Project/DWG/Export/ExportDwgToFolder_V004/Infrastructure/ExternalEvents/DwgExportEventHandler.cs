using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Services;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Infrastructure.ExternalEvents
{
    /// <summary>Which Revit-side action the handler runs next.</summary>
    public enum DwgExportRequest
    {
        /// <summary>Collect the DWG instances inside <see cref="DwgExportEventHandler.Scope"/>.</summary>
        Scan,

        /// <summary>Export the DWGs in <see cref="DwgExportEventHandler.ToExport"/>.</summary>
        Export,

        /// <summary>
        /// Apply the post-export review in <see cref="DwgExportEventHandler.Decisions"/> — deleting
        /// and/or converting the rows the user chose. Replaces V003's <c>DeleteElements</c>, which
        /// could only delete.
        /// </summary>
        ApplyPostExport
    }

    /// <summary>
    /// Performs all Revit-API work for the Export DWG to Folder tool.
    /// Scan reads the document within the chosen scope; Export writes only the DWGs the user ticked
    /// (isolating each one in its host view first — see <see cref="DwgExportService"/>);
    /// ApplyPostExport carries out the review the user confirmed after a successful export, deleting
    /// and/or converting rows to imports.
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

        /// <summary>
        /// The reviewed rows and what to do with each (ApplyPostExport only). Rows set to
        /// <see cref="PostExportAction.Keep"/> are carried along untouched so the ViewModel can map
        /// every result back, but nothing is done to them.
        /// </summary>
        public List<PostExportDecision> Decisions { get; set; } = new List<PostExportDecision>();

        /// <summary>
        /// View a model-space import is created in when a conversion has no host view of its own
        /// (ApplyPostExport only). The ViewModel leaves this null and the handler falls back to
        /// Revit's active view.
        /// </summary>
        public ElementId FallbackViewId { get; set; }

        // ---- Output properties read by the ViewModel after completion ----

        /// <summary>Instances left out of the last scan as duplicates of the same source file.</summary>
        public int DuplicatesSkipped { get; private set; }

        /// <summary>DWG instances found by the last scan.</summary>
        public List<DwgInstanceInfo> ScannedInstances { get; private set; } = new List<DwgInstanceInfo>();

        /// <summary>
        /// DWG/DXF export setups saved in the model, refreshed on every scan, with
        /// "(Revit defaults)" first. Needs a <c>Document</c>, which is why it comes back through the
        /// handler rather than being read in the ViewModel.
        /// </summary>
        public List<string> AvailableExportSetups { get; private set; } =
            new List<string> { DwgExportOptionsFactory.DefaultsLabel };

        /// <summary>
        /// Set when the last export could not use the chosen export setup and fell back to Revit
        /// defaults; null otherwise. Logged once as a Warning.
        /// </summary>
        public string OptionsWarning { get; private set; }

        /// <summary>DWGs written by the last export.</summary>
        public int ExportedCount { get; private set; }

        /// <summary>DWGs skipped by the last export (file already exists and Overwrite is off).</summary>
        public int SkippedCount { get; private set; }

        /// <summary>DWGs that failed in the last export.</summary>
        public int FailedCount { get; private set; }

        /// <summary>Of the reviewed rows, how many the last ApplyPostExport request deleted.</summary>
        public int DeletedCount { get; private set; }

        /// <summary>How many links the last ApplyPostExport request converted into imports.</summary>
        public int ConvertedCount { get; private set; }

        /// <summary>How many instances the last ApplyPostExport request re-linked to their exported file.</summary>
        public int RelinkedCount { get; private set; }

        /// <summary>How many requested conversions or re-links failed and rolled back on their own.</summary>
        public int ConvertFailedCount { get; private set; }

        /// <summary>
        /// Elements removed as a side effect of the last ApplyPostExport request that were not
        /// themselves reviewed — e.g. a dimension or tag hosted on a deleted CAD
        /// instance. <c>doc.Delete(id)</c> returns every element it actually removed, which can be
        /// more than the one requested; this is that difference, so the ViewModel can tell the user
        /// "plus N dependent element(s)" instead of silently deleting more than they confirmed.
        /// </summary>
        public int DependentDeletedCount { get; private set; }

        /// <summary>
        /// ElementId.Value of each element the last ApplyPostExport request was asked to delete but
        /// could not (already gone, or Revit refused it).
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

        /// <summary>
        /// Export of item <c>index</c> finished: error is null on success, "SKIP" when skipped,
        /// otherwise the failure message. The fourth argument is the route that produced the file
        /// ("copied source file" / "re-exported from view"), null when nothing was written.
        /// </summary>
        public event Action<int, int, string, string> ExportItemFinished;

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
                    case DwgExportRequest.ApplyPostExport:
                        ExecuteApplyPostExport(doc, app);
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
            ScannedInstances = collector.Collect(
                scope,
                (stage, current, total) => ScanProgress?.Invoke(stage, current, total),
                // V004: the output-name pattern is applied at scan time, so the grid shows the
                // names the export will actually write.
                (Settings ?? new ExportDwgSettings()).NameTemplate);
            DuplicatesSkipped = collector.DuplicatesSkipped;

            // V004: refresh the export-setup list from the model on every scan, so a setup saved in
            // Revit while the window was open shows up without reopening the tool.
            AvailableExportSetups = DwgExportOptionsFactory.ListSetups(doc);
        }

        // ── Export ──────────────────────────────────────────────────────────

        private void ExecuteExport(Autodesk.Revit.DB.Document doc)
        {
            ExportedCount = 0;
            SkippedCount = 0;
            FailedCount = 0;
            OptionsWarning = null;

            if (string.IsNullOrWhiteSpace(OutputFolder))
                throw new InvalidOperationException("No output folder selected.");

            var settings = Settings ?? new ExportDwgSettings();
            var exporter = new DwgExportService(doc, settings);
            var items = ToExport ?? new List<DwgInstanceInfo>();
            int total = items.Count;

            for (int i = 0; i < total; i++)
            {
                var info = items[i];
                ExportItemStarted?.Invoke(i, total);

                string destPath = BuildDestPath(OutputFolder, info, settings);
                string error = exporter.Export(info, destPath, settings.Overwrite, out string route);

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

                ExportItemFinished?.Invoke(i, total, error, route);
            }

            // Reported once per run, not once per row.
            OptionsWarning = exporter.OptionsWarning;
        }

        // ── Apply the post-export review ────────────────────────────────────

        /// <summary>
        /// Carries out the reviewed decisions: deletes the rows set to
        /// <see cref="PostExportAction.DeleteFromModel"/> and converts the rows set to
        /// <see cref="PostExportAction.ConvertToImport"/>.
        /// <para>
        /// The whole review is one <see cref="TransactionGroup"/> (ARCHITECTURE.md §5), assimilated
        /// when at least one decision succeeded and rolled back when none did — so a reviewed batch
        /// takes one Ctrl+Z to undo however many rows it touched. Inside it the deletes share one
        /// transaction, and each conversion gets its own, so one link that refuses to convert rolls
        /// back alone and the rest of the batch carries on.
        /// </para>
        /// </summary>
        private void ExecuteApplyPostExport(Autodesk.Revit.DB.Document doc, UIApplication app)
        {
            DeletedCount = 0;
            DependentDeletedCount = 0;
            ConvertedCount = 0;
            RelinkedCount = 0;
            ConvertFailedCount = 0;
            FailedDeleteIds = new List<long>();

            var decisions = Decisions ?? new List<PostExportDecision>();
            var toDelete = decisions.Where(d => d.Action == PostExportAction.DeleteFromModel).ToList();
            var toConvert = decisions
                .Where(d => d.Action == PostExportAction.ConvertToImport ||
                            d.Action == PostExportAction.ReplaceWithLinkToExported)
                .ToList();
            if (toDelete.Count == 0 && toConvert.Count == 0) return;

            var fallbackViewId = FallbackViewId ?? app?.ActiveUIDocument?.ActiveView?.Id
                                 ?? ElementId.InvalidElementId;

            using (var group = new TransactionGroup(doc, "Apply Exported CAD Review"))
            {
                group.Start();

                if (toDelete.Count > 0)
                    DeleteDecided(doc, toDelete);

                WorksharingGuard.TryCheckout(doc, toConvert.Where(d => d.ElementId != null).Select(d => d.ElementId).ToList());

                foreach (var decision in toConvert)
                {
                    if (decision.ElementId != null && !WorksharingGuard.CanEdit(doc, decision.ElementId, out string lockReason))
                    {
                        decision.Succeeded = false;
                        decision.Note = $"Not changed — {lockReason}";
                        ConvertFailedCount++;
                        continue;
                    }

                    bool toLink = decision.Action == PostExportAction.ReplaceWithLinkToExported;
                    string label = toLink
                        ? $"Replace CAD with link to exported file — {decision.Name}"
                        : $"Convert CAD link to import — {decision.Name}";

                    // One transaction per conversion: the converter deletes the original before it
                    // creates the replacement, so a half-done conversion must be able to roll back
                    // by itself without taking the rest of the batch with it.
                    using (var tx = new Transaction(doc, label))
                    {
                        tx.Start();
                        bool ok = false;
                        try
                        {
                            ok = toLink
                                ? CadInstanceConverter.ReplaceWithLinkToExportedFile(doc, decision, fallbackViewId)
                                : CadInstanceConverter.ConvertLinkToImport(doc, decision, fallbackViewId);
                        }
                        catch (Exception ex)
                        {
                            decision.Succeeded = false;
                            decision.Note = $"{(toLink ? "Re-link" : "Conversion")} failed ({ex.Message}) — this row was rolled back.";
                        }

                        if (ok)
                        {
                            tx.Commit();
                            if (toLink) RelinkedCount++;
                            else ConvertedCount++;
                        }
                        else
                        {
                            tx.RollBack();
                            ConvertFailedCount++;
                        }
                    }
                }

                if (DeletedCount > 0 || ConvertedCount > 0 || RelinkedCount > 0)
                    group.Assimilate();
                else
                    group.RollBack();
            }
        }

        /// <summary>
        /// Deletes the decided rows in one transaction. Elements that no longer exist, or that Revit
        /// refuses to delete, are recorded in <see cref="FailedDeleteIds"/> rather than stopping the
        /// whole batch — a confirmed-exported CAD instance that can't be deleted for some reason
        /// shouldn't block the rest from being cleaned up.
        /// </summary>
        private void DeleteDecided(Autodesk.Revit.DB.Document doc, List<PostExportDecision> toDelete)
        {
            var requested = new HashSet<long>(toDelete.Select(d => d.ElementIdValue));
            var allRemoved = new HashSet<long>();

            WorksharingGuard.TryCheckout(doc, toDelete.Where(d => d.ElementId != null).Select(d => d.ElementId).ToList());

            using (var tx = new Transaction(doc, "Delete Exported CAD Instances"))
            {
                tx.Start();
                foreach (var decision in toDelete)
                {
                    var id = decision.ElementId;
                    try
                    {
                        if (id != null && !WorksharingGuard.CanEdit(doc, id, out string lockReason))
                        {
                            FailedDeleteIds.Add(decision.ElementIdValue);
                            decision.Succeeded = false;
                            decision.Note = $"Not deleted — {lockReason}";
                            continue;
                        }

                        if (id == null || doc.GetElement(id) == null)
                        {
                            FailedDeleteIds.Add(decision.ElementIdValue);
                            decision.Succeeded = false;
                            decision.Note = "Already gone from the model.";
                            continue;
                        }

                        // doc.Delete returns every element actually removed, which can include
                        // dependents (a dimension/tag hosted on this CAD instance, etc.), not just
                        // the one id we asked for.
                        foreach (var removedId in doc.Delete(id))
                            allRemoved.Add(removedId.Value);

                        DeletedCount++;
                        decision.Succeeded = true;
                        decision.Note = "Deleted from the model.";
                    }
                    catch (Exception ex)
                    {
                        FailedDeleteIds.Add(decision.ElementIdValue);
                        decision.Succeeded = false;
                        decision.Note = $"Could not be deleted ({ex.Message}).";
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
