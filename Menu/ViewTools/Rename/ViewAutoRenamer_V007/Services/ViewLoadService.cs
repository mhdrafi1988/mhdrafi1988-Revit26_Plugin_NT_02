using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

/// <summary>
/// Collects the views shown in the grid. The current sheet's views load first (cheap) and the
/// rest of the project only when the user asks.
/// 7.3.0: sheet number and detail number come from the viewports, which are read once, instead
/// of two parameter reads per view; each view's type is read once; no view-scoped collector.
/// </summary>
public static class ViewLoadService
{
    private static readonly IReadOnlyList<string> NoSheets = new List<string>();

    /// <summary>
    /// Finds the sheet to load first: the active sheet, or the sheet the active
    /// view is placed on. Returns null when the active view is on no sheet.
    /// </summary>
    public static ViewSheet? FindCurrentSheet(Document doc, View? activeView)
    {
        if (activeView is ViewSheet sheet) return sheet;
        if (activeView == null) return null;

        var number = activeView.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER)?.AsString();
        if (string.IsNullOrWhiteSpace(number)) return null;

        // Revit filters the sheets by number itself, so only the matching sheet is opened
        // (before, every sheet in the project was read to compare its number).
        var rule = ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(BuiltInParameter.SHEET_NUMBER), number);
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .WherePasses(new ElementParameterFilter(rule))
            .Cast<ViewSheet>()
            .FirstOrDefault(s => s.SheetNumber == number);
    }

    /// <summary>Loads only the in-scope views placed on <paramref name="sheet"/>.</summary>
    public static List<ViewItemViewModel> LoadForSheet(Document doc, ViewSheet sheet)
    {
        // Legends/Schedules show only this sheet until "Load all views" fills in the rest.
        IReadOnlyList<string> onThisSheet = new List<string> { sheet.SheetNumber };
        var items = new List<ViewItemViewModel>();
        var seen = new HashSet<ElementId>();

        foreach (var vpId in sheet.GetAllViewports())
        {
            if (doc.GetElement(vpId) is not Viewport vp || !seen.Add(vp.ViewId)) continue;
            if (doc.GetElement(vp.ViewId) is not View v) continue;
            var item = BuildItem(v, onThisSheet, DetailNumberOf(vp));
            if (item != null) items.Add(item);
        }

        // Project-wide collector narrowed by Revit to this sheet's schedules. A collector
        // scoped to the sheet view makes Revit work out everything visible on the sheet first.
        foreach (var si in new FilteredElementCollector(doc)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .OwnedByView(sheet.Id)
                     .Cast<ScheduleSheetInstance>())
        {
            if (!seen.Add(si.ScheduleId)) continue;
            if (doc.GetElement(si.ScheduleId) is not View v) continue;
            var item = BuildItem(v, onThisSheet, null);
            if (item != null) items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Loads the project's in-scope views with their full sheet placement: all of them, or
    /// (7.4.0) only the view-type groups in <paramref name="groups"/>.
    /// </summary>
    /// <param name="doc">The project document.</param>
    /// <param name="groups">View-type groups to load; null loads every group.</param>
    /// <param name="cancel">Optional cancel helper; checked in every loop and reports progress. Throws <see cref="System.OperationCanceledException"/> after the user confirms a cancel.</param>
    public static List<ViewItemViewModel> LoadAll(Document doc, ToolCancellation? cancel = null, ISet<ViewTypeGroup>? groups = null)
    {
        cancel?.Report(0, 0, "Reading sheet placements…");
        var placed = ViewClassificationService.BuildPlacementLookup(doc, cancel);

        cancel?.Report(0, 0, "Reading views…");
        var views = CollectViews(doc, groups);

        var items = new List<ViewItemViewModel>(views.Count);
        for (int i = 0; i < views.Count; i++)
        {
            cancel?.Step(i, views.Count, "Reading view names…");
            if (views[i] is not View v) continue;

            ViewItemViewModel? item = placed.TryGetValue(v.Id, out var p)
                ? BuildItem(v, p.Sheets, p.DetailNumber, groups)
                : BuildItem(v, NoSheets, null, groups);
            if (item != null) items.Add(item);
        }
        return items;
    }

    /// <summary>
    /// Builds one grid row, or null for a view the tool does not rename (templates, sheets,
    /// 3D views…). Reads the view's type once.
    /// </summary>
    private static ViewItemViewModel? BuildItem(View v, IReadOnlyList<string> sheets, string? detailNumber,
        ISet<ViewTypeGroup>? groups = null)
    {
        if (v.IsTemplate) return null;
        var viewType = v.ViewType;
        var classified = ViewClassificationService.Classify(viewType);
        if (classified == null) return null;
        if (groups != null && !groups.Contains(classified.Value.group)) return null;

        // As before: Legends and Schedules can sit on several sheets, so they carry no detail number.
        bool multiPlaced = viewType == ViewType.Legend || viewType == ViewType.Schedule;

        return new ViewItemViewModel(
            v.Id, v.Name, viewType, classified.Value.group, classified.Value.display,
            sheets, multiPlaced ? null : detailNumber, v.Pinned);
    }

    /// <summary>
    /// Collects the candidate views. When only some groups are wanted, Revit hands back only the
    /// view classes those groups use (sections, plans, drafting views, schedules), so the other
    /// views are never opened. Legends have no class of their own, so asking for them reads all views.
    /// </summary>
    private static IList<Element> CollectViews(Document doc, ISet<ViewTypeGroup>? groups)
    {
        var collector = new FilteredElementCollector(doc);
        if (groups == null || groups.Contains(ViewTypeGroup.Legend))
            return collector.OfClass(typeof(View)).ToElements();

        var classes = new List<System.Type>();
        void Need(System.Type t) { if (!classes.Contains(t)) classes.Add(t); }
        foreach (var g in groups)
        {
            switch (g)
            {
                case ViewTypeGroup.SectionOrCallout:
                case ViewTypeGroup.Elevation:
                    Need(typeof(ViewSection)); break;
                case ViewTypeGroup.FloorPlan:
                case ViewTypeGroup.CeilingPlan:
                case ViewTypeGroup.StructuralPlan:
                case ViewTypeGroup.AreaPlan:
                    Need(typeof(ViewPlan)); break;
                case ViewTypeGroup.Drafting:
                    Need(typeof(ViewDrafting)); break;
                case ViewTypeGroup.Schedule:
                    Need(typeof(ViewSchedule)); break;
            }
        }
        if (classes.Count == 0) return new List<Element>();
        return collector.WherePasses(new ElementMulticlassFilter(classes)).ToElements();
    }

    /// <summary>The viewport's "Detail Number" (the same value the placed view reports).</summary>
    internal static string? DetailNumberOf(Viewport vp) =>
        vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString();
}
