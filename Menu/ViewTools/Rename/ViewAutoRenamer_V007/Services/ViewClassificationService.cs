using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.Shared.Services;
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
    ///
    /// Returns null for a view type the tool does not rename (sheets, 3D views…),
    /// so this is also the single "is this view in scope" test.
    /// </summary>
    public static (ViewTypeGroup group, string display)? Classify(ViewType viewType) => viewType switch
    {
        ViewType.Section         => (ViewTypeGroup.SectionOrCallout, "Section"),
        ViewType.Elevation       => (ViewTypeGroup.Elevation, "Elevation"),
        ViewType.FloorPlan       => (ViewTypeGroup.FloorPlan, "Floor Plan"),
        ViewType.CeilingPlan     => (ViewTypeGroup.CeilingPlan, "Ceiling Plan"),
        ViewType.EngineeringPlan => (ViewTypeGroup.StructuralPlan, "Structural Plan"),
        ViewType.AreaPlan        => (ViewTypeGroup.AreaPlan, "Area Plan"),
        ViewType.DraftingView    => (ViewTypeGroup.Drafting, "Drafting View"),
        ViewType.Legend          => (ViewTypeGroup.Legend, "Legend"),
        ViewType.Schedule        => (ViewTypeGroup.Schedule, "Schedule"),
        _                        => null, // not a view type this tool renames
    };

    /// <summary>Where a view sits: its sheet numbers (in sheet order) and, for a view placed once, its detail number.</summary>
    public sealed class Placement
    {
        /// <summary>Sheet numbers the view is placed on.</summary>
        public List<string> Sheets { get; } = new();
        /// <summary>Detail number of the view's first viewport; null for schedules.</summary>
        public string? DetailNumber { get; set; }
    }

    /// <summary>
    /// Builds a ViewId → placement lookup for every placed view, from two project-wide
    /// collectors: Viewport (all placed views, with the detail number) and
    /// ScheduleSheetInstance (schedules). 7.3.0: this replaces reading the sheet-number and
    /// detail-number parameters on every view, so unplaced views cost no parameter reads.
    /// </summary>
    /// <param name="doc">The project document.</param>
    /// <param name="cancel">Optional cancel helper; checked in every loop so a long lookup can be cancelled.</param>
    public static Dictionary<ElementId, Placement> BuildPlacementLookup(Document doc, ToolCancellation? cancel = null)
    {
        var result = new Dictionary<ElementId, Placement>();

        Placement? Add(ElementId viewId, string sheetNumber)
        {
            if (viewId == ElementId.InvalidElementId || string.IsNullOrWhiteSpace(sheetNumber)) return null;
            if (!result.TryGetValue(viewId, out var p))
            {
                p = new Placement();
                result[viewId] = p;
            }
            if (!p.Sheets.Contains(sheetNumber)) p.Sheets.Add(sheetNumber);
            return p;
        }

        // Sheet id -> sheet number. Placements below are matched against this,
        // which also skips anything whose owner is not a sheet.
        var sheetNumbers = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .ToDictionary(s => s.Id, s => s.SheetNumber);

        // Every placed view sits on its sheet via a Viewport.
        foreach (var vp in new FilteredElementCollector(doc)
                     .OfClass(typeof(Viewport))
                     .Cast<Viewport>())
        {
            cancel?.Check();
            if (!sheetNumbers.TryGetValue(vp.SheetId, out var number)) continue;
            var p = Add(vp.ViewId, number);
            if (p != null && p.DetailNumber == null)
                p.DetailNumber = ViewLoadService.DetailNumberOf(vp);
        }

        // Schedules sit on sheets via ScheduleSheetInstance (owner view = the sheet).
        foreach (var si in new FilteredElementCollector(doc)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .Cast<ScheduleSheetInstance>())
        {
            cancel?.Check();
            if (sheetNumbers.TryGetValue(si.OwnerViewId, out var number))
                Add(si.ScheduleId, number);
        }

        // Each view's sheets in sheet-collector order, so the grid's Sheet column
        // (first entry) shows the same sheet as before.
        var sheetOrder = sheetNumbers.Keys
            .Select((id, i) => (number: sheetNumbers[id], i))
            .GroupBy(x => x.number)
            .ToDictionary(g => g.Key, g => g.Min(x => x.i));
        foreach (var p in result.Values)
        {
            cancel?.Check();
            if (p.Sheets.Count > 1)
                p.Sheets.Sort((a, b) => sheetOrder[a].CompareTo(sheetOrder[b]));
        }

        return result;
    }
}
