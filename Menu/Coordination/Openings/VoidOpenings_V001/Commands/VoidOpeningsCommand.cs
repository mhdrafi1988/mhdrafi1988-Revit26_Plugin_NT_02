using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Extensions.DependencyInjection;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels;
using Revit26_Plugin_Revised.VoidOpenings.V001.UI.Views;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Commands
{
    /// <summary>
    /// Opens the Void Openings window (modeless): choose the structural and MEP models, set clearances,
    /// analyse the clashes, generate and cut the voids, export the results.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class VoidOpeningsCommand : IExternalCommand
    {
        private static VoidOpeningsWindow _openWindow;

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // The window does not exist yet, so these two cannot be shown in its notification strip.
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.VoidOpenings.Title, "Open a project first.");
                return Result.Cancelled;
            }
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.VoidOpenings.Title, "Void Openings works on project documents, not families.");
                return Result.Cancelled;
            }

            if (_openWindow != null)
            {
                _openWindow.Activate();
                return Result.Succeeded;
            }

            // Built here, inside the command: the dispatcher creates its external event, which needs a Revit API context.
            ServiceProvider services = CompositionRoot.Build(commandData.Application);
            try
            {
                var window = new VoidOpeningsWindow(services.GetRequiredService<MainViewModel>());
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Closed += (sender, args) =>
                {
                    _openWindow = null;
                    services.Dispose();
                };
                _openWindow = window;
                window.Show();
            }
            catch
            {
                _openWindow = null;
                services.Dispose();
                throw;
            }
            return Result.Succeeded;
        }
    }
}
