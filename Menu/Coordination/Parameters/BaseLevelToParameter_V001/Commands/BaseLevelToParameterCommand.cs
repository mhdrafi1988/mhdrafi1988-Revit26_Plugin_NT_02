using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Infrastructure;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Commands
{
    /// <summary>
    /// Opens Base Level to Parameter: lists the model elements of the active view (or the whole host
    /// model), reads each one's base level and base offset, and writes "&lt;Level&gt; (&lt;Offset&gt;)" to a
    /// text instance parameter.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BaseLevelToParameterCommand : IExternalCommand
    {
        // Static so the modeless window survives re-invocation and is not collected.
        private static BaseLevelToParameterWindow _window;

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.BaseLevelToParameter.Title, "Open a project first.");
                return Result.Cancelled;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.BaseLevelToParameter.Title, "Base Level to Parameter works on project documents, not families.");
                return Result.Cancelled;
            }

            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            var handler = new BaseLevelEventHandler();
            var externalEvent = ExternalEvent.Create(handler);
            var viewModel = new BaseLevelToParameterViewModel(handler, externalEvent, doc.Title);
            viewModel.Initialize(doc, uidoc.ActiveView);

            _window = new BaseLevelToParameterWindow(viewModel);
            _window.Closed += (_, _) => { _window = null; externalEvent.Dispose(); };
            _window.Show();
            return Result.Succeeded;
        }
    }
}
