using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Views;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Commands;

/// <summary>
/// Opens View Auto Renamer V007. V007 = V006 with faster opening: one document-wide
/// sheet-placement scan and a capped grid height on first layout.
/// </summary>
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

    /// <summary>Ribbon entry point; runs through ToolGuard so no exception reaches Revit.</summary>
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) =>
        ToolGuard.RunCommand(typeof(OpenViewAutoRenamerCommand), c, ref m, e, ExecuteInternal);

    private static Result ExecuteInternal(ExternalCommandData c, ref string m, ElementSet e)
    {
        var uidoc = c.Application.ActiveUIDocument;
        if (uidoc?.Document == null)
        {
            m = "Open a project before running View Auto Renamer.";
            return Result.Failed;
        }
        var doc = uidoc.Document;

        RevitEventManager.Initialize();

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
