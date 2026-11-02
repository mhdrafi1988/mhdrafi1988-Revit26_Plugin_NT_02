using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Commands
{
    /// <summary>
    /// Ribbon entry point for Bubble Auto Renumber V010. Thin by design — opens the
    /// modeless window and nothing else; every decision lives in Core and every
    /// model change in Infrastructure/ExternalEvents (CLAUDE.md).
    ///
    /// CHANGED in V009:
    /// - Wrapped in ToolGuard.RunCommand rather than a local try/catch + Utilities.Logger,
    ///   so a failure here reports and logs the same way as every other tool's.
    /// - The handler and ExternalEvent now come from BubbleAutoRenumberEventManager
    ///   instead of being created on each click.
    ///
    /// Renamed from V008's SectionAutoRenumberCommand: the tool, folder, namespace,
    /// ribbon button and ToolCatalog entry all say BubbleAutoRenumber, and only the
    /// class names said Section — per Rafi's confirmed decision (2026-10-16).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BubbleAutoRenumberCommand : IExternalCommand
    {
        /// <summary>One window at a time; a second click activates the open one.</summary>
        private static BubbleAutoRenumberWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(typeof(BubbleAutoRenumberCommand), commandData, ref message, elements, Run);

        private static Result Run(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_window != null)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;

            if (uidoc?.Document == null)
            {
                message = "Open a project before running Bubble Auto Renumber.";
                return Result.Failed;
            }

            _window = new BubbleAutoRenumberWindow(uidoc, uiapp);
            _window.Closed += (_, _) => _window = null;
            _window.Show();

            return Result.Succeeded;
        }
    }
}
