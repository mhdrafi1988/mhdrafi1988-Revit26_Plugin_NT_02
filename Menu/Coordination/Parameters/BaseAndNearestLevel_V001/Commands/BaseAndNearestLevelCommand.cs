using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Commands
{
    /// <summary>
    /// Opens Base and Nearest Level: lists the roofs, floors and walls of the active view (or the whole host
    /// model), reads each one's base level and offset, works out the project level nearest its real base
    /// elevation, and writes both pairs to four parameters. A separate action re-hosts elements onto their
    /// nearest level without moving them.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BaseAndNearestLevelCommand : IExternalCommand
    {
        // Static so the modeless window survives re-invocation and is not collected.
        private static BaseAndNearestLevelWindow _window;

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.BaseAndNearestLevel.Title, "Open a project first.");
                return Result.Cancelled;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.BaseAndNearestLevel.Title,
                    "Base and Nearest Level works on project documents, not families.");
                return Result.Cancelled;
            }

            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var handler = new BaseAndNearestEventHandler();
            var externalEvent = ExternalEvent.Create(handler);
            var viewModel = new ViewModel_BaseAndNearestLevel(handler, externalEvent, doc.Title);
            viewModel.Initialize(doc, uidoc.ActiveView);

            _window = new BaseAndNearestLevelWindow(viewModel);
            _window.Closed += (_, _) => { _window = null; externalEvent.Dispose(); };
            _window.Show();
            return Result.Succeeded;
        }
    }
}
