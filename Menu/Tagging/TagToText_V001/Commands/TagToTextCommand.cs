using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.TagToText.V001.Core.Services;
using Revit26_Plugin_Revised.TagToText.V001.UI.ViewModels;
using Revit26_Plugin_Revised.TagToText.V001.UI.Views;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.TagToText.V001.Commands
{
    /// <summary>
    /// Opens the Tag To Text window, which copies spot elevations, spot coordinates, spot slopes and
    /// tags into text notes with leaders, in the same view or a new duplicate of it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagToTextCommand : IExternalCommand
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
                TaskDialog.Show(ToolCatalog.TagToText.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.TagToText.Title, "The active document is read-only.");
                return Result.Cancelled;
            }

            string viewProblem = TagReader.CheckView(doc.ActiveView);
            if (viewProblem != null)
            {
                TaskDialog.Show(ToolCatalog.TagToText.Title, viewProblem);
                return Result.Cancelled;
            }

            var viewModel = new TagToTextViewModel(uidoc);
            var window = new TagToTextWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
