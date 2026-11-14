using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.Events;
using Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.Revit;
using Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.ViewModels;
using Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.Commands
{
    /// <summary>
    /// Tapered EPS 2D Pattern — draws the tapered EPS board pattern as detail lines in a plan view.
    /// Runs the entry checks, reads the roof, then opens the modeless window. All model changes
    /// happen later through <see cref="RunPatternHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class TaperedEpsPattern2DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument? uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                Show("Open a project and a plan view, then run the tool again.");
                return Result.Cancelled;
            }

            try
            {
                Document doc = uidoc.Document;

                // 1. Plan view
                if (!PlanViewValidator.IsSupported(doc.ActiveView, out ViewPlan? view, out string viewMessage) || view == null)
                {
                    Show(viewMessage);
                    return Result.Cancelled;
                }

                // 2. Roof (preselection or pick)
                RoofBase? roof = RoofPicker.Pick(uidoc, out string pickMessage);
                if (roof == null)
                {
                    Show(pickMessage);
                    return Result.Cancelled;
                }

                // 3. Footprint roof, 4. visible in the active view
                if (!RoofContextBuilder.TryCheckRoof(roof, view, out string roofMessage))
                {
                    Show(roofMessage);
                    return Result.Cancelled;
                }

                // Read everything the modeless window needs while we are in a valid API context.
                RoofContext context = RoofContextBuilder.Build(doc, view.Id, roof.Id);
                IReadOnlyList<LineStyleOption> styles = LineStyleProvider.GetStyles(doc);
                IReadOnlyList<TextNoteTypeOption> textTypes = TextNoteTypeProvider.GetTypes(doc);

                // External events: created once here, owned by this window (no static state).
                var runHandler = new RunPatternHandler();
                var refreshHandler = new RefreshRoofHandler();
                ExternalEvent runEvent = ExternalEvent.Create(runHandler);
                ExternalEvent refreshEvent = ExternalEvent.Create(refreshHandler);

                var viewModel = new TaperedEpsPattern2DViewModel(
                    context, styles, textTypes, runHandler, runEvent, refreshHandler, refreshEvent);

                var window = new TaperedEpsPattern2DWindow(viewModel);
                new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };
                window.Closed += (_, _) =>
                {
                    viewModel.Detach();
                    runEvent.Dispose();
                    refreshEvent.Dispose();
                };
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                Show("The tool could not start:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static void Show(string text) =>
            TaskDialog.Show(TaperedEpsPattern2DToolInfo.Title, text);
    }
}
