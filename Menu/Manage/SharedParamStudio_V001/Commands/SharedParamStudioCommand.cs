using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure;
using Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SharedParamStudio.V001.UI.Views;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Commands
{
    /// <summary>
    /// Opens Shared Parameter Studio: create a shared parameter and bind it on the go, merge two
    /// shared parameter files (and add new parameters), check a file for duplicates, and retrieve
    /// the shared parameters used in the open project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SharedParamStudioCommand : IExternalCommand
    {
        // Static so the modeless window survives re-invocation and is not collected.
        private static SharedParamStudioWindow _window;

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.SharedParamStudio.Title, "Open a project first.");
                return Result.Cancelled;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.SharedParamStudio.Title, "Shared Parameter Studio works on project documents, not families.");
                return Result.Cancelled;
            }

            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var handler = new StudioExternalEventHandler();
            var externalEvent = ExternalEvent.Create(handler);
            var viewModel = new SharedParamStudioViewModel(handler, externalEvent, SharedParamStudioService.GetBindableCategories(doc),
                SharedParamStudioService.GetProjectParameterNames(doc));

            _window = new SharedParamStudioWindow(viewModel) { Owner = null };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            return Result.Succeeded;
        }
    }
}
