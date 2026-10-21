using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels;
using Revit26_Plugin_Revised.TrackerId.V002.UI.Views;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.TrackerId.V002.Commands
{
    /// <summary>
    /// Opens the Tracker ID window: gives every placed element of the chosen category and types one
    /// permanent Tracker_ID (plus zone, level, grid and source-GUID helper parameters) and exports
    /// them as a CSV for Power BI. Confirmed design (2026-10-20): one ID per element,
    /// stored in shared text parameter Tracker_ID; zone from the Zone parameter or the scope box;
    /// grid from the nearest grid crossing to the element's centre; three free custom segments.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrackerIdCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.TrackerIdV002.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.TrackerIdV002.Title, "Tracker ID works on project documents, not families.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.TrackerIdV002.Title, "The active document is read-only.");
                return Result.Cancelled;
            }

            // Created here, inside the command: the view model reads the model and creates its external
            // event, and both need a Revit API context.
            var viewModel = new TrackerIdViewModel(uidoc);
            var window = new TrackerIdWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
