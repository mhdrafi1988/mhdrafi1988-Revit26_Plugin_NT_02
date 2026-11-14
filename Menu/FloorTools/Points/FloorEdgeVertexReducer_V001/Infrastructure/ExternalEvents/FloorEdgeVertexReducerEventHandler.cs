// =======================================================
// File: FloorEdgeVertexReducerEventHandler.cs
// Location: Infrastructure/ExternalEvents/
// Ported unchanged from V003 (namespace + moved from Commands/ to
// Infrastructure/ExternalEvents/, matching this suite's vertical-slice
// convention — logic itself is untouched). Note this tool's
// instance-scoped ExternalEvent (one Handler+ExternalEvent created per
// Command invocation, held by the ViewModel) rather than a static
// singleton EventManager — already correct, avoids global mutable
// state, kept as-is.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Models;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Services;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Infrastructure.ExternalEvents
{
    public enum ReducerRequest
    {
        None,
        SelectFloor,
        Preview,
        Apply
    }

    /// <summary>
    /// Every Revit API call for this tool goes through here — never called directly
    /// from the WPF UI thread. Raise() on the ExternalEvent queues Execute() to run
    /// on Revit's own thread.
    /// </summary>
    public class FloorEdgeVertexReducerEventHandler : IExternalEventHandler
    {
        private readonly MainViewModel _vm;

        public ReducerRequest PendingRequest { get; set; } = ReducerRequest.None;

        private Element _floor;
        private List<VertexDecision> _lastDecisions;

        public FloorEdgeVertexReducerEventHandler(MainViewModel vm)
        {
            _vm = vm;
        }

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            // Esc / Cancel with a Yes-No confirm during Preview and Apply (picking keeps Revit's own Esc).
            using ToolCancellation cancel = PendingRequest is ReducerRequest.Preview or ReducerRequest.Apply
                ? ToolCancellation.BeginFor(ToolCatalog.FloorEdgeVertexReducer, app)
                : null;
            try
            {
                switch (PendingRequest)
                {
                    case ReducerRequest.SelectFloor:
                        HandleSelectFloor(app.ActiveUIDocument);
                        break;
                    case ReducerRequest.Preview:
                        HandlePreview(app.ActiveUIDocument.Document);
                        break;
                    case ReducerRequest.Apply:
                        HandleApply(app.ActiveUIDocument.Document);
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // user cancelled the pick — no log needed
            }
            catch (OperationCanceledException)
            {
                // The transaction group in HandleApply is rolled back as the exception unwinds.
                _vm.AddLog(LogLevel.Warning, "Cancelled by user. Nothing was changed.");
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                _vm.AddLog(LogLevel.Error, $"Unexpected error: {ex.Message}");
            }
            finally
            {
                PendingRequest = ReducerRequest.None;
            }
        }

        private void HandleSelectFloor(UIDocument uidoc)
        {
            var reference = uidoc.Selection.PickObject(
                ObjectType.Element,
                new FloorSelectionFilter(),
                "Select a floor with shape editing enabled");

            _floor = uidoc.Document.GetElement(reference);
            _lastDecisions = null;

            // Floor port: refuse floors that can't be changed (read-only, borrowed, ...).
            string blockReason = FloorShapeEditChecks.GetBlockReason(_floor as Floor);
            if (blockReason != null)
            {
                _vm.AddLog(LogLevel.Error, $"Floor {_floor?.Name} (id {_floor?.Id.Value}) can't be changed: {blockReason}.");
                _floor = null;
                return;
            }

            string name = _floor?.Name ?? "Unknown";
            _vm.SetSelectedFloor($"Floor: {name} (id {_floor.Id.Value})");
            _vm.AddLog(LogLevel.Info, $"Floor selected: {name} (id {_floor.Id.Value})");
        }

        private void HandlePreview(Document doc)
        {
            if (_floor == null)
            {
                _vm.AddLog(LogLevel.Warning, "No floor selected.");
                return;
            }

            var footprint = _floor as Floor;
            var editor = footprint?.GetSlabShapeEditor();
            if (editor == null || !editor.IsEnabled)
            {
                _vm.AddLog(LogLevel.Error, "Selected floor does not have shape editing enabled.");
                return;
            }

            var segments = EdgeVertexReducerService.BuildSegments(_floor, out var skipped);
            foreach (var s in skipped)
                _vm.AddLog(LogLevel.Warning, s);

            double toleranceFeet = EdgeVertexReducerService.MmToFeet(_vm.ToleranceMm);
            _lastDecisions = EdgeVertexReducerService.ClassifyAndReduce(editor, segments, toleranceFeet, _vm.NeighborOffset);

            _vm.PopulatePreview(_lastDecisions);

            int keep = _lastDecisions.Count(d => !d.WillRemove);
            int remove = _lastDecisions.Count(d => d.WillRemove);
            _vm.SetSummary($"{keep} kept · {remove} pending removal");
            _vm.AddLog(LogLevel.Success, $"Preview complete: {segments.Count} straight segments scanned, {remove} point(s) marked for removal");
        }

        private void HandleApply(Document doc)
        {
            if (_floor == null || _lastDecisions == null)
            {
                _vm.AddLog(LogLevel.Warning, "Run Preview before Apply.");
                return;
            }

            var footprint = _floor as Floor;
            var editor = footprint?.GetSlabShapeEditor();
            if (editor == null)
            {
                _vm.AddLog(LogLevel.Error, "Shape editor unavailable.");
                return;
            }

            using var tg = new TransactionGroup(doc, "Floor edge vertex reducer");
            tg.Start();

            using (var t = new Transaction(doc, "Remove interior shape points"))
            {
                var startStatus = t.Start();
                if (startStatus != TransactionStatus.Started)
                {
                    _vm.AddLog(LogLevel.Error, "Could not start transaction.");
                    tg.RollBack();
                    return;
                }

                int removed = EdgeVertexReducerService.ApplyRemovals(
                    editor, _lastDecisions, msg => _vm.AddLog(LogLevel.Warning, msg));

                var commitStatus = t.Commit();
                if (commitStatus != TransactionStatus.Committed)
                {
                    _vm.AddLog(LogLevel.Error, $"Transaction failed to commit: {commitStatus}");
                    tg.RollBack();
                    return;
                }

                tg.Assimilate();

                _vm.AddLog(LogLevel.Success, $"Changes applied — {removed} point(s) removed");
                int kept = _lastDecisions.Count(d => !d.WillRemove);
                _vm.SetSummary($"{kept} kept · {removed} removed");
                _lastDecisions = null;
                return;
            }
        }

        public string GetName() => "Floor Edge Vertex Reducer Event Handler";
    }
}
