using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Handlers;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Views;
using System;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SectionAutoRenumberCommand : IExternalCommand
    {
        // Single window instance — prevents opening duplicates
        private static SectionAutoRenumberWindow? _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_window != null)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                UIApplication uiapp = commandData.Application;
                UIDocument    uidoc = uiapp.ActiveUIDocument;

                if (uidoc == null)
                {
                    TaskDialog.Show("Section Auto Renumber", "No active document.");
                    return Result.Failed;
                }

                var handler       = new SectionAutoRenumberHandler();
                var externalEvent = ExternalEvent.Create(handler);

                _window = new SectionAutoRenumberWindow(uidoc, uiapp, handler, externalEvent);
                _window.Closed += (_, _) => _window = null;
                _window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("SectionAutoRenumberCommand", ex);
                TaskDialog.Show("Section Auto Renumber — Error", ex.Message);
                return Result.Failed;
            }
        }
    }
}
