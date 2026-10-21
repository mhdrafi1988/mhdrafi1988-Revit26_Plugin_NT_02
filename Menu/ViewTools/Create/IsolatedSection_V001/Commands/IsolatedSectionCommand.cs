using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.IsolatedSection.V001.Infrastructure.Revit;
using Revit26_Plugin_Revised.IsolatedSection.V001.UI.ViewModels;
using Revit26_Plugin_Revised.IsolatedSection.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Commands
{
    /// <summary>
    /// Opens the Isolated Section window with the elements selected in Revit. The tool makes one 3D view
    /// holding every selected element: its section box is turned in plan to be parallel to the largest side
    /// of any selected element (or a side or face the user chooses), the camera faces that side, and the
    /// elements are isolated (temporary by default).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IsolatedSectionCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.IsolatedSection.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.IsolatedSection.Title, "Isolated Section works on project documents, not families.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.IsolatedSection.Title, "The active document is read-only, so no view can be created.");
                return Result.Cancelled;
            }

            // Created here, inside the command: the view model reads the model and creates its external
            // event, and both need a Revit API context.
            var viewModel = new IsolatedSectionViewModel(uidoc, SelectionService.ReadSelection(uidoc));
            var window = new IsolatedSectionWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
