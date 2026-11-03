using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V006.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V006.Services;
using Revit26_Plugin_Revised.ViewAutoRenamer.V006.ViewModels;
using Revit26_Plugin_Revised.ViewAutoRenamer.V006.Views;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V006.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class OpenViewAutoRenamerCommand : IExternalCommand
{
    // Exact ViewType values in scope, per confirmed requirements:
    // Sections & Callouts, Plans (Floor/Ceiling/Structural/Area), Elevations,
    // Drafting, Legends, Schedules.
    private static readonly HashSet<ViewType> InScopeViewTypes = new()
    {
        ViewType.Section,
        ViewType.Elevation,
        ViewType.FloorPlan,
        ViewType.CeilingPlan,
        ViewType.EngineeringPlan,   // Structural Plan
        ViewType.AreaPlan,
        ViewType.DraftingView,
        ViewType.Legend,
        ViewType.Schedule,
    };
    // NOTE: Callouts of sections report ViewType.Section (no separate
    // ViewType exists for them) and are included automatically by this
    // filter. See ClassifyView for the SectionOrCallout grouping rationale.

    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) =>
        ToolGuard.RunCommand(GetType(), c, ref m, e, ExecuteCore);

    private Result ExecuteCore(ExternalCommandData c, ref string m, ElementSet e)
    {
        try
        {
            return ExecuteInternal(c, ref m, e);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (System.Exception ex)
        {
            Logger.Error("OpenViewAutoRenamerCommand", ex);
            m = ex.Message;
            TaskDialog.Show("View Auto Renamer", $"An unexpected error occurred: {ex.Message}");
            return Result.Failed;
        }
    }

    private Result ExecuteInternal(ExternalCommandData c, ref string m, ElementSet e)
    {
        RevitEventManager.Initialize();

        var uidoc = c.Application.ActiveUIDocument;
        var doc   = uidoc.Document;

        var activeSheet       = uidoc.ActiveView as ViewSheet;
        string activeSheetNum = activeSheet?.SheetNumber ?? "";

        // ── Build sheet-placement lookup for Legend/Schedule (can appear on
        // multiple sheets — ViewSheet.GetAllPlacedViews / ScheduleSheetInstance) ──
        var placedSheetsByViewId = ViewClassificationService.BuildPlacedSheetsLookup(doc);

        // ── Collect all in-scope views ──────────────────────────────────────
        var allViews = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => !v.IsTemplate && InScopeViewTypes.Contains(v.ViewType))
            .ToList();

        var items = new List<ViewItemViewModel>();
        foreach (var v in allViews)
        {
            var (group, display) = ViewClassificationService.ClassifyView(v);

            IReadOnlyList<string> placedSheets;
            string? detailNumber = null;

            if (v.ViewType == ViewType.Legend || v.ViewType == ViewType.Schedule)
            {
                // Legends/Schedules: look up from the multi-sheet lookup table.
                placedSheets = placedSheetsByViewId.TryGetValue(v.Id, out var list)
                    ? list
                    : new List<string>();
            }
            else
            {
                // Sections/Callouts/Elevations/Plans/Drafting: single placement
                // via VIEWER_SHEET_NUMBER, same approach as V012.
                var sheetNum = v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER)?.AsString();
                detailNumber = v.get_Parameter(BuiltInParameter.VIEWER_DETAIL_NUMBER)?.AsString();
                placedSheets = string.IsNullOrWhiteSpace(sheetNum)
                    ? new List<string>()
                    : new List<string> { sheetNum };
            }

            items.Add(new ViewItemViewModel(v, group, display, placedSheets, detailNumber));
        }

        var vm = new ViewsListViewModel(items, activeSheetNum);
        new ViewsListWindow(vm).Show();

        return Result.Succeeded;
    }
}
