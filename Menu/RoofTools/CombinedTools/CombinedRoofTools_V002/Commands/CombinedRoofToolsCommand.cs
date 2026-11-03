// =======================================================
// File: CombinedRoofToolsCommand.cs
// Location: Commands/
// Entry point for the combined window: picks one roof, builds all 5
// tools' ViewModels for it via CombinedRoofToolsInitializer (same
// pattern each tool's own standalone Command already uses — pre-window
// setup runs synchronously here, inside this Execute() call's own valid
// API context), then shows the modeless tabbed window.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.CombinedRoofTools.V002.Core;
using Revit26_Plugin_Revised.CombinedRoofTools.V002.UI.ViewModels;
using Revit26_Plugin_Revised.CombinedRoofTools.V002.UI.Views;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CombinedRoofTools.V002.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CombinedRoofToolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element, new CombinedRoofSelectionFilter(), "Select a RoofBase element");

                if (pickedRef == null) return Result.Cancelled;

                RoofBase roof = doc.GetElement(pickedRef) as RoofBase;
                if (roof == null)
                {
                    message = "Selected element is not a RoofBase.";
                    return Result.Failed;
                }

                var initial = CombinedRoofToolsInitializer.BuildAll(uiApp, roof);
                var vm = new CombinedRoofToolsViewModel(uiApp, initial);

                var window = new CombinedRoofToolsWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("CombinedRoofToolsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
