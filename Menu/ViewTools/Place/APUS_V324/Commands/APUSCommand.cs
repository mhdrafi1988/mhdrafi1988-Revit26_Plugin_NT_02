// File: AutoPlaceSectionsCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.APUS.V324.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.APUS.V324.Commands
{
    /// <summary>
    /// Revit command entry point. All Revit API calls happen here, on the
    /// Revit thread; the UI is created after data is loaded.
    ///
    /// V321 fix: passes commandData.Application.MainWindowHandle through to
    /// the window constructor so WindowInteropHelper gets Revit's actual main
    /// window handle, per the suite's window-ownership convention — V320
    /// substituted Process.GetCurrentProcess().MainWindowHandle instead.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoPlaceSectionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Execute(commandData.Application, ref message, elements, out _);
        }

        public Result Execute(
            UIApplication uiApp,
            ref string message,
            ElementSet elements,
            out Views.AutoPlaceSectionsWindow createdWindow)
        {
            createdWindow = null;

            try
            {
                UIDocument uidoc = uiApp.ActiveUIDocument;

                if (uidoc == null || uidoc.Document == null)
                {
                    TaskDialog.Show("APUS V321", "No active Revit document found.");
                    return Result.Failed;
                }

                if (uidoc.Document.IsFamilyDocument)
                {
                    TaskDialog.Show("APUS V321",
                        "This command only works in project documents, not family documents.");
                    return Result.Failed;
                }

                // --- ALL REVIT API CALLS HERE (Revit thread — safe) ---

                var sectionService = new SectionCollectionService(uidoc.Document);
                List<ViewModels.SectionItemViewModel> sections = sectionService.Collect();

                var titleBlockService = new TitleBlockCollectionService(uidoc.Document);
                var titleBlocks = titleBlockService.Collect();

                // --- UI CREATION — no Revit API calls beyond this point ---

                var viewModel = new ViewModels.AutoPlaceSectionsViewModel(
                    uidoc,
                    sections,
                    titleBlocks);

                createdWindow = new Views.AutoPlaceSectionsWindow(
                    viewModel,
                    uiApp.MainWindowHandle);

                createdWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("AutoPlaceSectionsCommand", ex);
                TaskDialog.Show("APUS V321 — Error",
                    $"Failed to start Auto Place Sections:\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>Invokes the command programmatically (e.g. from a ribbon refresh button).</summary>
        public static Result Invoke(UIApplication uiApp)
        {
            var command = new AutoPlaceSectionsCommand();
            string message = null;
            var elements = new ElementSet();
            return command.Execute(uiApp, ref message, elements, out _);
        }

        public string GetName() => "APUS V321 — Auto Place Sections";
    }
}
