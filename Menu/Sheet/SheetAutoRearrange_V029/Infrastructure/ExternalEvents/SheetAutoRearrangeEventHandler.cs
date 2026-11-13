using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Engine;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Infrastructure.ExternalEvents
{
    public enum SheetAutoRearrangeAction
    {
        LoadViewsOnSheet,
        RunRearrange
    }

    /// <summary>
    /// Single IExternalEventHandler for Sheet Auto Rearrange.
    ///
    /// V009 CHANGE: ManualRegionOverride and the RedetectRegion action are
    /// removed entirely — the Placeable Area UI (including manual fallback
    /// and Re-detect) is gone, per explicit request. Detection now runs
    /// silently as an internal step of Load and Run, with no user-facing
    /// controls. Algorithm field removed — Sheet Order is hardcoded in
    /// RearrangeEngine.
    /// </summary>
    public class SheetAutoRearrangeEventHandler : IExternalEventHandler
    {
        public SheetAutoRearrangeAction Action { get; set; }

        public ViewSheet? TargetSheet { get; set; }
        public List<ViewOnSheetItem>? LoadedItems { get; private set; }

        public PlaceableRegion? Region { get; private set; }
        public bool MultipleTitleBlocksFound { get; private set; }
        public bool NoTitleBlockFound { get; private set; }

        public List<ViewOnSheetItem>? ItemsToProcess { get; set; }
        public OverflowHandlingMode OverflowHandlingMode { get; set; }
        public GapSettings? GapSettings { get; set; }
        public double RowToleranceMm { get; set; }
        public RowAlignment RowAlignment { get; set; }
        public BlockAlignmentH ColumnAlignmentH { get; set; }
        public BlockAlignmentV ColumnAlignmentV { get; set; }

        public RowFillStrategy RowFillStrategy { get; set; }

        public LeftoverGroupMode LeftoverGroupMode { get; set; }
        public double ColumnToleranceMm { get; set; }
        public BlockAlignmentH WithinColumnAlign { get; set; }

        /// <summary>V026 NEW — see RearrangeEngine.Run/SheetOrderPackingService.BuildPriorityGroups.</summary>
        public bool EnablePriorityGroups { get; set; }

        /// <summary>V026 NEW — ranked ViewType placement order, only used when EnablePriorityGroups is true.</summary>
        public List<ViewType> PriorityOrder { get; set; } = new();

        /// <summary>V029: Align &amp; Distribute options for the Run. Null = off.</summary>
        public AlignDistributeSettings? AlignDistribute { get; set; }

        public ObservableCollection<LogEntry>? Log { get; set; }
        public RunResult? LastRunResult { get; private set; }

        /// <summary>Unhooks the window's Revit event subscriptions. Runs inside Execute, where Revit allows it.</summary>
        public Action<UIApplication>? DetachCallback { get; set; }

        /// <summary>Set when the window closes: the next Execute runs <see cref="DetachCallback"/>.</summary>
        public bool DetachRequested { get; set; }

        /// <summary>True when nothing else is pending, so the next Execute only detaches.</summary>
        public bool DetachOnly { get; set; }

        /// <summary>True once <see cref="DetachCallback"/> has run; the window is gone and no result is wanted.</summary>
        public bool Detached { get; private set; }

        public event Action? Completed;

        private readonly ViewportCollectorService _collector = new();
        private readonly TitleBlockDetectionService _detectionService = new();
        private readonly RearrangeEngine _engine = new();

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (DetachRequested && DetachOnly)
            {
                RunDetach(app);
                Completed?.Invoke();
                return;
            }

            var doc = app.ActiveUIDocument.Document;

            // Esc / Cancel with a Yes-No confirm while Run rearranges; the engine's transaction is rolled back.
            using ToolCancellation cancel = Action == SheetAutoRearrangeAction.RunRearrange
                ? ToolCancellation.BeginFor(ToolCatalog.SheetAutoRearrangeV029, app)
                : null;
            try
            {
                switch (Action)
                {
                    case SheetAutoRearrangeAction.LoadViewsOnSheet:
                        ExecuteLoad(doc);
                        break;

                    case SheetAutoRearrangeAction.RunRearrange:
                        ExecuteRun(doc);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                LastRunResult = new RunResult { Success = false, ErrorMessage = "Cancelled by user. Nothing was changed." };
                Log?.Add(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                throw; // ToolGuard treats it as a user cancel; finally still completes the request
            }
            finally
            {
                if (DetachRequested)
                    RunDetach(app);
                Completed?.Invoke();
            }
        }

        /// <summary>Runs the window's detach callback inside the API context. Never throws: the window is already closed.</summary>
        private void RunDetach(UIApplication app)
        {
            try
            {
                DetachCallback?.Invoke(app);
            }
            catch (Exception ex)
            {
                Revit26_Plugin_Revised.Utilities.Logger.Error("SheetAutoRearrangeEventHandler.Detach", ex);
            }
            finally
            {
                DetachRequested = false;
                Detached = true;
            }
        }

        private void ExecuteLoad(Document doc)
        {
            if (TargetSheet == null)
            {
                LoadedItems = new List<ViewOnSheetItem>();
                Region = null;
                return;
            }

            LoadedItems = _collector.CollectViewsOnSheet(doc, TargetSheet);
            ExecuteDetectRegion(doc);
        }

        private void ExecuteDetectRegion(Document doc)
        {
            if (TargetSheet == null || GapSettings == null)
            {
                Region = null;
                return;
            }

            var detection = _detectionService.Detect(doc, TargetSheet, GapSettings);
            MultipleTitleBlocksFound = detection.MultipleTitleBlocksFound;
            NoTitleBlockFound = detection.NoTitleBlockFound;
            Region = detection.Region;
        }

        private void ExecuteRun(Document doc)
        {
            if (TargetSheet == null || ItemsToProcess == null || GapSettings == null || Log == null)
            {
                LastRunResult = new RunResult { Success = false, ErrorMessage = "Missing required Run parameters." };
                return;
            }

            ExecuteDetectRegion(doc);

            if (MultipleTitleBlocksFound)
            {
                LastRunResult = new RunResult
                {
                    Success = false,
                    ErrorMessage = "Sheet has multiple title blocks — Run skipped, no changes made."
                };
                return;
            }

            LastRunResult = _engine.Run(
                doc,
                TargetSheet,
                ItemsToProcess,
                OverflowHandlingMode,
                GapSettings,
                RowToleranceMm,
                RowAlignment,
                ColumnAlignmentH,
                ColumnAlignmentV,
                RowFillStrategy,
                LeftoverGroupMode,
                ColumnToleranceMm,
                WithinColumnAlign,
                EnablePriorityGroups,
                PriorityOrder,
                AlignDistribute,
                Region,
                Log);
        }

        public string GetName() => "Sheet Auto Rearrange Event Handler";
    }
}
