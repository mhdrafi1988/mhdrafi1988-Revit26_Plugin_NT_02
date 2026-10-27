using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofTag.V018.Core;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofTag.V018
{
    /// <summary>
    /// Places spot-elevation tags on a roof's shape-editor points. This class only validates the
    /// session, picks the roof and opens the window; the work is done by <see cref="RoofTagWorkflow"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoofTagCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private static Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string title = ToolCatalog.RoofTag.Title;
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc?.Document;

            string problem = RoofTagPreflight.CheckSession(doc);
            if (problem != null)
            {
                TaskDialog.Show(title, problem);
                return Result.Cancelled;
            }

            RoofBase roof = SelectionHelper.SelectRoof(uiDoc);
            if (roof == null)
            {
                TaskDialog.Show(title, "No roof selected.");
                return Result.Cancelled;
            }

            problem = RoofTagPreflight.EnsureShapeEditorEnabled(doc, roof);
            if (problem != null)
            {
                TaskDialog.Show(title, problem);
                return Result.Cancelled;
            }

            var window = new RoofTagWindow(uiApp);
            window.Show();

            var viewModel = (RoofTagViewModel)window.DataContext;
            return new RoofTagWorkflow(uiDoc, viewModel).Run(roof);
        }
    }
}
