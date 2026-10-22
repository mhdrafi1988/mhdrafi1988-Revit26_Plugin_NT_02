using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

/// <summary>
/// Extracted from OpenViewAutoRenamerCommand — view classification and
/// sheet-placement lookup are business logic, not command orchestration.
/// </summary>
public static class ViewClassificationService
{
    /// <summary>
    /// Classifies a view into its ViewTypeGroup (for dup-check + filter) and
    /// a human display label (for the grid pill/column).
    ///
    /// NOTE: Callouts report ViewType.Section in the Revit API — there is no
    /// separate ViewType for them, and reliably distinguishing a callout
    /// from a plain section requires walking dependent elements (fragile).
    /// Per confirmed decision, both are labeled "Section" and share one
    /// duplicate-name group (SectionOrCallout), which matches Revit's
    /// actual name-uniqueness rule regardless of the cosmetic label.
    /// </summary>
    public static (ViewTypeGroup group, string display) ClassifyView(View v)
    {
        switch (v.ViewType)
        {
            case ViewType.Section:
                return (ViewTypeGroup.SectionOrCallout, "Section");

            case ViewType.Elevation:
                return (ViewTypeGroup.Elevation, "Elevation");

            case ViewType.FloorPlan:
                return (ViewTypeGroup.FloorPlan, "Floor Plan");

            case ViewType.CeilingPlan:
                return (ViewTypeGroup.CeilingPlan, "Ceiling Plan");

            case ViewType.EngineeringPlan:
                return (ViewTypeGroup.StructuralPlan, "Structural Plan");

            case ViewType.AreaPlan:
                return (ViewTypeGroup.AreaPlan, "Area Plan");

            case ViewType.DraftingView:
                return (ViewTypeGroup.Drafting, "Drafting View");

            case ViewType.Legend:
                return (ViewTypeGroup.Legend, "Legend");

            case ViewType.Schedule:
                return (ViewTypeGroup.Schedule, "Schedule");

            default:
                // Callers filter to exactly the 9 types handled above, so this
                // should be unreachable. Fail loudly rather than silently
                // mis-bucketing an unexpected view type into an unrelated
                // duplicate-name group.
                throw new System.InvalidOperationException(
                    $"Unhandled ViewType '{v.ViewType}' reached ClassifyView — " +
                    "update the in-scope ViewType set and ClassifyView together if a new type was added.");
        }
    }

    /// <summary>
    /// Builds a ViewId → list-of-sheet-numbers lookup for views that can be
    /// placed on multiple sheets (Legends via Viewport, Schedules via
    /// ScheduleSheetInstance). Section/Callout/Elevation/Plan/Drafting views
    /// use the simpler VIEWER_SHEET_NUMBER parameter directly (single place).
    /// </summary>
    public static Dictionary<ElementId, List<string>> BuildPlacedSheetsLookup(Document doc)
    {
        var result = new Dictionary<ElementId, List<string>>();

        void Add(ElementId viewId, string sheetNumber)
        {
            if (viewId == ElementId.InvalidElementId || string.IsNullOrWhiteSpace(sheetNumber)) return;
            if (!result.TryGetValue(viewId, out var list))
            {
                list = new List<string>();
                result[viewId] = list;
            }
            if (!list.Contains(sheetNumber)) list.Add(sheetNumber);
        }

        // Sheet id -> sheet number. Placements below are matched against this,
        // which also skips anything whose owner is not a sheet.
        var sheetNumbers = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .ToDictionary(s => s.Id, s => s.SheetNumber);

        // V007: two document-wide collectors instead of two per-sheet ones.
        // Per-sheet (view-scoped) collectors cost one model scan each, which made
        // opening the tool slow on projects with many sheets. Same result.

        // Legends (and every other placed view) sit on sheets via Viewport.
        foreach (var vp in new FilteredElementCollector(doc)
                     .OfClass(typeof(Viewport))
                     .Cast<Viewport>())
        {
            if (sheetNumbers.TryGetValue(vp.SheetId, out var number))
                Add(vp.ViewId, number);
        }

        // Schedules sit on sheets via ScheduleSheetInstance (owner view = the sheet).
        foreach (var si in new FilteredElementCollector(doc)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .Cast<ScheduleSheetInstance>())
        {
            if (sheetNumbers.TryGetValue(si.OwnerViewId, out var number))
                Add(si.ScheduleId, number);
        }

        // Keep V006's order: each view's sheets in sheet-collector order, so the
        // grid's Sheet column (first entry) shows the same sheet as before.
        var sheetOrder = sheetNumbers.Keys
            .Select((id, i) => (number: sheetNumbers[id], i))
            .GroupBy(x => x.number)
            .ToDictionary(g => g.Key, g => g.Min(x => x.i));
        foreach (var list in result.Values)
            list.Sort((a, b) => sheetOrder[a].CompareTo(sheetOrder[b]));

        return result;
    }
}
