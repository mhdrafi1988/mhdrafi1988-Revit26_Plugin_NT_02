using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Models;
using Revit26_Plugin_Revised.FloorViewFocus.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorViewFocus.V001.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Commands
{
    /// <summary>
    /// Thin Revit-side entry point. Validates the active view (plan only) and the
    /// pre-selection (one or more floors), then opens the modeless window.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorViewFocusCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                if (doc.ActiveView is not ViewPlan view || view.IsTemplate)
                {
                    TaskDialog.Show(FloorViewFocusDefaults.Title,
                        "Open a plan view (floor, ceiling, engineering or area plan) and run the tool again.");
                    return Result.Cancelled;
                }

                var floors = new List<FloorInfo>();
                int ignored = 0;
                foreach (ElementId id in uidoc.Selection.GetElementIds())
                {
                    if (doc.GetElement(id) is Floor floor)
                    {
                        string description = floor.FloorType is FloorType rt
                            ? $"{rt.FamilyName} : {rt.Name}"
                            : floor.Name;

                        floors.Add(new FloorInfo
                        {
                            Id = floor.Id.Value,
                            UniqueId = floor.UniqueId,
                            Description = description
                        });
                    }
                    else
                    {
                        ignored++;
                    }
                }

                if (floors.Count == 0)
                {
                    TaskDialog.Show(FloorViewFocusDefaults.Title,
                        "Select one or more floors, then run the tool again.");
                    return Result.Cancelled;
                }

                floors = floors.OrderBy(r => r.Id).ToList();

                var vm = new FloorViewFocusViewModel(
                    viewUniqueId: view.UniqueId,
                    viewName: view.Name,
                    viewTypeText: Regex.Replace(view.ViewType.ToString(), "(?<=[a-z])(?=[A-Z])", " "),
                    scopeBoxName: GetScopeBoxName(doc, view),
                    floors: floors,
                    ignoredCount: ignored);

                var window = new FloorViewFocusWindow(vm);
                new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
                window.Show();

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("FloorViewFocusCommand", ex);
                message = ex.Message;
                TaskDialog.Show(FloorViewFocusDefaults.Title, $"The tool could not start.\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>Returns the scope box name, or an empty string when None.</summary>
        private static string GetScopeBoxName(Document doc, View view)
        {
            Parameter? p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            ElementId? id = p?.AsElementId();
            if (id == null || id.Value == ElementId.InvalidElementId.Value) return string.Empty;
            return doc.GetElement(id)?.Name ?? $"Id {id.Value}";
        }
    }
}
