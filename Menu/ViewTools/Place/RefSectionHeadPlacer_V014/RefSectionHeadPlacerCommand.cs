using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V014.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RefSectionHeadPlacer.V014.Commands
{
    /// <summary>
    /// Ribbon entry point (bind a PushButtonData's ClassName to this type).
    ///
    /// NOTE (ExternalEvent placement): ExternalEvent.Create() itself is called
    /// inside RefSectionHeadPlacerViewModel's constructor, not textually inside
    /// this Execute() method. That still satisfies the "create inside
    /// IExternalCommand.Execute()" rule, because the VM is constructed by
    /// RefSectionHeadPlacerWindow's constructor, which is only ever invoked here,
    /// on Revit's API thread, during this Execute() call. Flagging this so it's
    /// not mistaken for a rule violation on review.
    ///
    /// Modeless: window is shown with Show(), never ShowDialog(), so Execute()
    /// returns immediately while the tool stays open.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RefSectionHeadPlacerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                Document doc = uiApp.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                var window = new RefSectionHeadPlacerWindow(uiApp, doc);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("RefSectionHeadPlacerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
