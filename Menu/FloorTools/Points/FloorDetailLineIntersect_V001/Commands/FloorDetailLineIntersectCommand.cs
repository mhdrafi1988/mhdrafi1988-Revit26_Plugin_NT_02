using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorDetailLineIntersectCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc      = commandData.Application.ActiveUIDocument;
            Document   doc        = uiDoc.Document;
            View       activeView = doc.ActiveView;

            // ── Guard: must be a plan view ──────────────────────────────────
            if (activeView.ViewType != ViewType.FloorPlan  &&
                activeView.ViewType != ViewType.CeilingPlan &&
                activeView.ViewType != ViewType.AreaPlan)
            {
                TaskDialog.Show("Floor Detail Line Intersect",
                    "Active view must be a Plan View.\nPlease switch to a floor plan and retry.");
                return Result.Cancelled;
            }

            // ── Step 1: select one Floor ──────────────────────────────────────
            Reference floorRef;
            try
            {
                floorRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new FloorSelectionFilter(),
                    "Select one Floor element");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            Element floorElem = doc.GetElement(floorRef.ElementId);
            if (floorElem is not Floor floor)
            {
                TaskDialog.Show("Floor Detail Line Intersect", "Selected element is not a floor.");
                return Result.Cancelled;
            }

            // Floor port: refuse floors that can't be changed (group, read-only,
            // borrowed by another user, no shape-editing support).
            string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
            if (blockReason != null)
            {
                TaskDialog.Show("Floor Detail Line Intersect", $"Floor {floor.Name} (Id {floor.Id.Value}) can't be changed: {blockReason}.");
                return Result.Cancelled;
            }

            // ── Step 2: multi-select Detail Lines ───────────────────────────
            IList<Reference> lineRefs;
            try
            {
                lineRefs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new DetailLineSelectionFilter(),
                    "Multi-select Detail Lines, then press Finish");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (lineRefs == null || lineRefs.Count == 0)
            {
                TaskDialog.Show("Floor Detail Line Intersect", "No Detail Lines selected.");
                return Result.Cancelled;
            }

            var detailLineIds = lineRefs
                .Select(r => doc.GetElement(r.ElementId))
                .OfType<DetailLine>()
                .Select(dl => dl.Id)
                .ToList();

            // ── Launch window ────────────────────────────────────────────────
            var vm     = new FloorDetailLineIntersectViewModel(floor.Id, detailLineIds, floor.Name, doc.ActiveView?.Name);
            var window = new FloorDetailLineIntersectWindow(vm);
            window.Show();

            return Result.Succeeded;
        }
    }
}
