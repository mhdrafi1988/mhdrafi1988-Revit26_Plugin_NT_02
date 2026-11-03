// =======================================================
// File: InnerLoopsAndPerpendicularCommand.cs
// Location: Commands/
// Renamed from PerpendicularPointCommand (V003) — matches this suite's
// namespace/tool-name convention (also matches the class name the V004
// ghost reference in the ribbon already used).
//
// Changes vs V003:
//   FIX  Window shown modeless (.Show()) instead of modal (.ShowDialog()).
//        The V003 ViewModel called RoofGeometryService/
//        PerpendicularPointService directly from RelayCommands, which
//        only worked because ShowDialog() kept this Execute() call's own
//        valid Revit API context alive for the whole window lifetime.
//        Modeless windows cannot call the Revit API directly from a
//        button click — Analyze, Generate, and Apply now go through
//        InnerLoopsAndPerpendicularEventManager/Handler instead (see
//        Infrastructure/ExternalEvents).
//   FIX  Window parented via WindowInteropHelper to Revit's main window,
//        per this suite's standing convention (previously not set).
//   NOTE The first Analyze pass still runs synchronously here, inside
//        this Execute() call's own valid API context — avoids an
//        unnecessary ExternalEvent round-trip just to populate the
//        window on first open. The "Re-analyze" button inside the
//        modeless window uses the ExternalEvent, since by then this
//        method has already returned.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Core.Services;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.UI.ViewModels;
using Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.UI.Views;
using System;
using System.Linq;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Commands
{
    /// <summary>
    /// External command that launches the Inner Loops And Perpendicular tool.
    /// Prompts the user to pick a roof, enables shape editing, flattens
    /// existing vertices, runs the first loop analysis, then opens the
    /// modeless window. No TaskDialogs are raised; all feedback is
    /// surfaced in the activity log.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InnerLoopsAndPerpendicularCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document   doc   = uidoc.Document;

            try
            {
                // ── Pick roof ────────────────────────────────────────────────
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element, "Select a RoofBase element");

                if (pickedRef == null) return Result.Cancelled;

                RoofBase roof = doc.GetElement(pickedRef) as RoofBase;
                if (roof == null)
                {
                    message = "Selected element is not a RoofBase.";
                    return Result.Failed;
                }

                // ── Enable shape editing & flatten ───────────────────────────
                using (Transaction tx = new Transaction(doc, "Enable Shape Editing"))
                {
                    tx.Start();

                    var editor = roof.GetSlabShapeEditor();
                    if (!editor.IsEnabled)
                        editor.Enable();

                    foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                        editor.ModifySubElement(v, 0.0);

                    tx.Commit();
                }

                // ── First analysis, still inside a valid API context ────────
                var geometryService = new RoofGeometryService();
                var allLoops = geometryService.ExtractCircularLoops(roof);
                var outerLoop = allLoops.FirstOrDefault(l => l.LoopType == "Outer");
                var innerLoops = allLoops.Where(l => l.LoopType == "Inner").ToList();

                // ── Launch UI (modeless) ─────────────────────────────────────
                var vm = new InnerLoopsAndPerpendicularViewModel(commandData.Application, roof.Id, innerLoops, outerLoop, circlesOnlyByDefault: true);

                var window = new InnerLoopsAndPerpendicularWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("InnerLoopsAndPerpendicularCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
