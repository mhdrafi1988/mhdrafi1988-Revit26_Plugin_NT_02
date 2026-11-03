using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.UI.ViewModels;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Commands
{
    /// <summary>
    /// Ribbon entry point for Detail Line Closed Loop V003: checks the active document and opens the
    /// tool window. Runs through <see cref="ToolGuard"/> so no error reaches Revit unhandled.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DetailLineClosedLoopCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument?.Document;

            if (doc == null)
            {
                TaskDialog.Show("Detail Line Closed Loop", "No active document.");
                return Result.Failed;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show("Detail Line Closed Loop", "Document is read-only.");
                return Result.Failed;
            }

            var vm = new DetailLineClosedLoopViewModel(uiApp);
            var window = new DetailLineClosedLoopWindow { DataContext = vm };

            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.Show();

            return Result.Succeeded;
        }
    }
}
