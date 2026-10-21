using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Revit26_Plugin_Revised.CreateSections.V013.Helpers;
using Revit26_Plugin_Revised.CreateSections.V013.Models;
using Revit26_Plugin_Revised.CreateSections.V013.ViewModels;

namespace Revit26_Plugin_Revised.CreateSections.V013.Services
{
    /// <summary>
    /// Coordinates the full per-line pipeline: orientation -> host search
    /// (with V008 threshold-fallback) -> section creation -> naming ->
    /// cleanup, updating live metrics on the ViewModel as it goes.
    ///
    /// V008 changes from V07:
    /// - Runs from IExternalEventHandler.Execute() (see SectionCreationExternalEvent),
    ///   not directly off the VM's CreateRequested event on the UI thread â€”
    ///   this is what makes the live log actually live and Cancel actually work.
    /// - Increments VM metric counters (Selected/Created/Skipped/Failed/Renamed)
    ///   as each line resolves, for the metric cards.
    /// - Uses HostElementSearchService's new below/passing-through Z rule and
    ///   4-category HostSourceSelection instead of SnapSourceMode.
    /// - Falls back to threshold-based height (via SectionCreationService)
    ///   when no host qualifies and UseThresholdFallback is checked, instead
    ///   of unconditionally skipping the line.
    ///
    /// V009 changes from V008:
    /// - Start() now takes the full IList&lt;Reference&gt; PLUS reads
    ///   _vm.PreviewRows to determine which lines are actually included â€”
    ///   per confirmed spec, the "Sections To Create" grid's checked rows
    ///   are the source of truth for Create, not the raw PickObjects
    ///   selection. Unchecked/filtered-out lines are silently excluded
    ///   from the run entirely (not even counted as Skipped, since they
    ///   were never submitted for creation).
    /// - Passes the 7 new View Crop properties into SectionCreationOptions.
    ///
    /// V010 fix (metrics/log dialog):
    /// - The whole per-line loop runs synchronously inside one
    ///   IExternalEventHandler.Execute() call, so WPF never got a chance
    ///   to repaint metric card bindings mid-loop â€” only the live log
    ///   appeared "live", because LiveLogService explicitly pushes each
    ///   entry via System.Windows.Application.Current.Dispatcher.Invoke. Metric writes
    ///   now go through the same UpdateUi(...) dispatch so cards update
    ///   per line, not just at the end.
    /// - ExportLog() no longer opens OpenFolderDialog automatically. It
    ///   silently writes to LogSaveFolder if the user already picked one
    ///   (via the new Browse button), otherwise falls back to a default
    ///   folder under My Documents\Revit26_Plugin_Revised\CreateSectionsFromDetailLines\Logs.
    ///   No dialog is ever shown from here.
    /// </summary>
    public class SectionFromLineOrchestrator
    {
        /// <summary>Default log folder used when the user has never picked one via Browse.</summary>
        private static readonly string DefaultLogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Revit26_Plugin_Revised", "CreateSectionsFromDetailLines", "Logs");

        /// <summary>Marshals a VM mutation onto the UI thread so bound controls (metric cards) repaint immediately.</summary>
        private static void UpdateUi(Action action) => System.Windows.Application.Current.Dispatcher.Invoke(action);

        private readonly Document _doc;
        private readonly ViewPlan _plan;
        private readonly SectionFromLineViewModel _vm;
        private readonly LiveLogService _log;

        public SectionFromLineOrchestrator(
            Document doc,
            ViewPlan plan,
            SectionFromLineViewModel vm)
        {
            _doc = doc;
            _plan = plan;
            _vm = vm;
            _log = new LiveLogService(vm.LiveLog);
        }

        public void Start(IList<Reference> refs)
        {
            if (!_vm.ValidateInputs(out string err))
            {
                _log.Error(err);
                return;
            }

            // V009: the grid's checked rows are the source of truth for
            // Create, not the raw refs passed in from PickObjects. A row
            // is only in PreviewRows if the last scan resolved it; if the
            // user never ran a scan (PreviewRows empty), fall back to the
            // full refs list so Create still works without requiring a
            // manual Refresh first.
            IList<Reference> included = _vm.PreviewRows.Count > 0
                ? _vm.PreviewRows
                    .Where(row => row.IsIncluded)
                    .Select(row => row.SourceReference)
                    .ToList()
                : refs;

            if (included.Count == 0)
            {
                _log.Warn("No sections checked in the preview grid â€” nothing to create.");
                return;
            }

            UpdateUi(() =>
            {
                _vm.ResetMetrics();
                _vm.SelectedCount = included.Count;
                _vm.IsRunning = true;
            });
            _vm.Execution.Reset();

            var hostSource = new HostSourceSelection
            {
                FloorHost = _vm.SearchFloorHost,
                RoofHost = _vm.SearchRoofHost,
                FloorLinked = _vm.SearchFloorLinked,
                RoofLinked = _vm.SearchRoofLinked
            };

            var options = new SectionCreationOptions
            {
                Prefix = _vm.SectionPrefix,
                FarClipMm = _vm.FarClipMm,
                TopPaddingMm = _vm.TopPaddingMm,
                BottomPaddingMm = _vm.BottomPaddingMm,
                BottomOffsetMm = _vm.BottomOffsetMm,
                SearchThresholdMm = _vm.SearchThresholdMm,
                HostSource = hostSource,
                UseThresholdFallback = _vm.UseThresholdFallback,
                SectionType = _vm.SelectedSectionType,
                Template = _vm.SelectedTemplate,
                OpenAfterCreate = _vm.OpenAllAfterCreate,
                DeleteLinesAfterCreate = _vm.DeleteLinesAfterCreate,
                ViewScale = _vm.ViewScale,
                CropBoxActive = _vm.CropBoxActive,
                CropRegionVisible = _vm.CropRegionVisible,
                AnnotationCropActive = _vm.AnnotationCropActive,
                TopAnnotationOffsetMm = _vm.TopAnnotationOffsetMm,
                BottomAnnotationOffsetMm = _vm.BottomAnnotationOffsetMm,
                LeftAnnotationOffsetMm = _vm.LeftAnnotationOffsetMm,
                RightAnnotationOffsetMm = _vm.RightAnnotationOffsetMm
            };

            var orientSvc = new SectionOrientationService();
            var hostSvc = new HostElementSearchService(_doc);
            var createSvc = new SectionCreationService(_doc, _plan);
            var cleanup = new PostCreationCleanupService(_doc);

            var created = new List<ElementId>();
            UIDocument uiDoc = new UIDocument(_doc);

            using TransactionGroup tg =
                new(_doc, "Create Sections From Lines");
            tg.Start();

            foreach (var r in included)
            {
                if (_vm.Execution.Token.IsCancellationRequested)
                {
                    _log.Warn("Cancelled by user.");
                    break;
                }

                if (_doc.GetElement(r) is not DetailLine dl ||
                    dl.GeometryCurve is not Line line)
                    continue;

                _log.Info($"Processing line {dl.Id.Value}");

                var orient = orientSvc.Calculate(line);
                if (!orient.Success)
                {
                    _log.Warn($"Line {dl.Id.Value}: could not compute orientation. Skipped.");
                    UpdateUi(() => _vm.SkippedCount++);
                    continue;
                }

                var candidates = hostSvc.FindCandidates(
                    orient.MidPoint,
                    orient.MidPoint.Z,
                    options.SearchThresholdMm,
                    options.HostSource);

                var host = candidates.FirstOrDefault();

                if (host == null && !options.UseThresholdFallback)
                {
                    _log.Warn($"Line {dl.Id.Value}: no qualifying host found. Skipped.");
                    UpdateUi(() => _vm.SkippedCount++);
                    continue;
                }

                if (host == null)
                    _log.Warn($"Line {dl.Id.Value}: no qualifying host found â€” using threshold fallback ({options.SearchThresholdMm}mm).");

                using Transaction tx =
                    new(_doc, "Create Section");
                tx.Start();

                var req = new SectionCreationRequest
                {
                    SourceLine = dl,
                    GeometryLine = line,
                    Orientation = orient,
                    HostElement = host,
                    Options = options
                };

                var result = createSvc.Create(req, out bool renamed, out bool usedFallback);

                if (!result.Success)
                {
                    _log.Error($"Line {dl.Id.Value}: {result.ErrorMessage}");
                    UpdateUi(() => _vm.FailedCount++);
                    tx.RollBack();
                    continue;
                }

                if (renamed)
                {
                    _log.Warn($"Renamed to {result.Section.Name}");
                    UpdateUi(() => _vm.RenamedCount++);
                }

                created.Add(dl.Id);
                UpdateUi(() => _vm.CreatedCount++);
                _log.Success($"Created {result.Section.Name}"
                    + (usedFallback ? " (threshold fallback)" : string.Empty));

                tx.Commit();

                if (options.OpenAfterCreate)
                    uiDoc.ActiveView = result.Section;
            }

            tg.Assimilate();

            cleanup.DeleteDetailLines(
                created,
                options.DeleteLinesAfterCreate);

            UpdateUi(() => _vm.IsRunning = false);

            _log.Info(
                $"Completed. {_vm.CreatedCount} created | " +
                $"{_vm.SkippedCount} skipped | {_vm.FailedCount} failed | " +
                $"{_vm.RenamedCount} renamed");

            _vm.SaveSettings();
            ExportLog();
        }

        /// <summary>
        /// V010 fix: auto-saves the log on completion silently â€” no dialog.
        /// Uses _vm.LogSaveFolder if the user already picked one via the
        /// Browse button (persisted to settings.json); otherwise falls back
        /// to DefaultLogFolder. Assumption: falling back silently (rather
        /// than skipping export) matches the suite's "auto-save log on
        /// completion" convention â€” flag if a skip-until-Browse behavior
        /// is preferred instead.
        /// </summary>
        private void ExportLog()
        {
            string folder = string.IsNullOrWhiteSpace(_vm.LogSaveFolder)
                ? DefaultLogFolder
                : _vm.LogSaveFolder;

            try
            {
                var fileService = new LogFileService();
                string path = fileService.Write(_vm.LiveLog, folder);
                _log.Info($"Log saved to {path}");
            }
            catch (Exception ex)
            {
                _log.Warn($"Log export failed: {ex.Message}");
            }
        }
    }
}

