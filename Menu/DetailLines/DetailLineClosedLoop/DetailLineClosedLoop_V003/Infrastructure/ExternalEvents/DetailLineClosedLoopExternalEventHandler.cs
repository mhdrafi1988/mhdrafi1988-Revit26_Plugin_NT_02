using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Engine;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Models;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Services;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Infrastructure.SelectionFilters;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.UI.ViewModels;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Single handler for both Revit-side actions this tool needs (picking
    /// lines in the view, and running the trim/merge/close pipeline + draw) —
    /// shared per the project's "one handler/event pair unless a window has
    /// multiple distinct Revit-side actions" convention.
    /// </summary>
    public class DetailLineClosedLoopExternalEventHandler : IExternalEventHandler
    {
        private readonly DetailLineClosedLoopViewModel _vm;

        public DetailLineClosedLoopRequest PendingRequest { get; set; } = DetailLineClosedLoopRequest.None;

        public DetailLineClosedLoopExternalEventHandler(DetailLineClosedLoopViewModel vm) => _vm = vm;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            Document doc = uiDoc?.Document;

            try
            {
                if (doc == null)
                {
                    _vm.Log(LogLevel.Error, "No active document — request aborted.");
                    return;
                }

                switch (PendingRequest)
                {
                    case DetailLineClosedLoopRequest.SelectLines:
                        RunSelectLines(uiDoc, doc);
                        break;
                    case DetailLineClosedLoopRequest.Run:
                        RunProcess(uiDoc, doc);
                        break;
                    case DetailLineClosedLoopRequest.DeleteSelectedLines:
                        RunDeleteSelectedLines(doc);
                        break;
                    case DetailLineClosedLoopRequest.RefreshCreatedLines:
                        RunRefreshCreatedLines(doc);
                        break;
                }
            }
            catch (Exception ex)
            {
                _vm.Log(LogLevel.Error, $"Unhandled error: {ex.Message}");
            }
            finally
            {
                PendingRequest = DetailLineClosedLoopRequest.None;
                _vm.IsBusy = false;
            }
        }

        private void RunSelectLines(UIDocument uiDoc, Document doc)
        {
            try
            {
                IList<Reference> picked = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new DetailCurveSelectionFilter(),
                    "Select detail lines/arcs forming the boundary, then click Finish");

                var ids = picked.Select(r => r.ElementId).ToList();
                _vm.SetSelection(ids);

                int lineCount = ids.Count(id => (doc.GetElement(id) as DetailCurve)?.GeometryCurve is Line);
                int arcCount = ids.Count(id => (doc.GetElement(id) as DetailCurve)?.GeometryCurve is Arc);
                _vm.Log(LogLevel.Info, $"Selected {ids.Count} curve(s) ({lineCount} Line, {arcCount} Arc)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                _vm.Log(LogLevel.Info, "Selection cancelled.");
            }
        }

        private void RunProcess(UIDocument uiDoc, Document doc)
        {
            View view = doc.ActiveView;

            if (_vm.SelectedCurveIds.Count == 0)
            {
                _vm.Log(LogLevel.Warning, "Run aborted: no lines selected. Click 'Select Detail Lines' first.");
                _vm.ApplyRunResult(new ProcessResult { Success = false, FailedCount = 1, ErrorMessage = "No selection." });
                return;
            }

            ProcessResult result = DetailLineClosedLoopEngine.Run(
                doc, view, _vm.SelectedCurveIds, _vm.SnapEndpoints, _vm.EffectiveGapToleranceFeet, _vm.LogEntries);

            if (result.Success)
            {
                using var tg = new TransactionGroup(doc, "Draw Closed Detail Line Loop");
                tg.Start();
                bool drawOk = false;

                using (var t = new Transaction(doc, "Draw Detail Lines"))
                {
                    t.Start();
                    try
                    {
                        result.CreatedElementIds = DetailLineDrawService.Draw(doc, view, result.Loop);
                        t.Commit();
                        drawOk = true;
                        _vm.Log(LogLevel.Success, $"Drew {result.CreatedElementIds.Count} new Detail Lines forming closed loop (originals kept)");
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        result.Success = false;
                        result.FailedCount = 1;
                        result.ErrorMessage = $"Draw failed, transaction rolled back: {ex.Message}";
                        _vm.Log(LogLevel.Error, result.ErrorMessage);
                    }
                }

                if (drawOk)
                {
                    if (_vm.GroupNewLines && result.CreatedElementIds.Count > 0)
                    {
                        using var gt = new Transaction(doc, "Group New Lines");
                        gt.Start();
                        try
                        {
                            GroupCreationService.CreateGroup(doc, result.CreatedElementIds, _vm.GroupName, out string finalName);
                            result.GroupName = finalName;
                            gt.Commit();
                            _vm.Log(LogLevel.Info, $"Group \"{finalName}\" created ({result.CreatedElementIds.Count} member(s)).");
                        }
                        catch (Exception ex)
                        {
                            gt.RollBack();
                            _vm.Log(LogLevel.Warning, $"Group creation skipped — lines were still drawn: {ex.Message}");
                        }
                    }

                    tg.Assimilate();

                    List<CreatedLineItem> rows = BuildCreatedLineItems(doc, result.CreatedElementIds);
                    _vm.SetCreatedLines(rows);
                    _vm.Log(LogLevel.Info, $"Created Lines grid populated with {rows.Count} row(s).");
                }
                else
                {
                    tg.RollBack();
                }
            }

            _vm.ApplyRunResult(result);
        }

        private static List<CreatedLineItem> BuildCreatedLineItems(Document doc, List<ElementId> ids)
        {
            var rows = new List<CreatedLineItem>();
            int index = 1;
            foreach (ElementId id in ids)
            {
                if (doc.GetElement(id) is DetailCurve dc)
                {
                    Curve c = dc.GeometryCurve;
                    string typeName = c is Arc ? "Arc" : "Line";
                    double lengthMm = UnitUtils.ConvertFromInternalUnits(c.Length, UnitTypeId.Millimeters);
                    rows.Add(new CreatedLineItem(id, index, typeName, lengthMm));
                }
                index++;
            }
            return rows;
        }

        private void RunDeleteSelectedLines(Document doc)
        {
            List<ElementId> ids = _vm.GetCheckedCreatedLineIds();
            if (ids.Count == 0)
            {
                _vm.Log(LogLevel.Warning, "Delete Selected clicked with nothing checked.");
                return;
            }

            using var t = new Transaction(doc, "Delete Selected Lines");
            t.Start();
            try
            {
                doc.Delete(ids);
                t.Commit();
                _vm.RemoveCreatedLines(ids);
                _vm.Log(LogLevel.Success, $"Deleted {ids.Count} selected line(s).");
            }
            catch (Exception ex)
            {
                t.RollBack();
                _vm.Log(LogLevel.Error, $"Delete failed, transaction rolled back: {ex.Message}");
            }
        }

        private void RunRefreshCreatedLines(Document doc)
        {
            List<ElementId> stale = _vm.GetAllCreatedLineIds().Where(id => doc.GetElement(id) == null).ToList();
            if (stale.Count > 0)
            {
                _vm.RemoveCreatedLines(stale);
                _vm.Log(LogLevel.Info, $"Refresh: removed {stale.Count} row(s) no longer present in the model.");
            }
            else
            {
                _vm.Log(LogLevel.Info, "Refresh: all rows still present in the model.");
            }
        }

        public string GetName() => "DetailLineClosedLoop External Event Handler";
    }
}
