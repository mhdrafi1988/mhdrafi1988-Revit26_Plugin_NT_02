using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

/// <summary>
/// Collects the views shown in the grid. 7.1.0 loads the current sheet's views
/// first (cheap) and the rest of the project only when the user asks.
/// </summary>
public static class ViewLoadService
{
    // Sections & Callouts, Plans (Floor/Ceiling/Structural/Area), Elevations,
    // Drafting, Legends, Schedules. Callouts report ViewType.Section.
    private static readonly HashSet<Autodesk.Revit.DB.ViewType> InScopeViewTypes = new()
    {
        Autodesk.Revit.DB.ViewType.Section,
        Autodesk.Revit.DB.ViewType.Elevation,
        Autodesk.Revit.DB.ViewType.FloorPlan,
        Autodesk.Revit.DB.ViewType.CeilingPlan,
        Autodesk.Revit.DB.ViewType.EngineeringPlan,
        Autodesk.Revit.DB.ViewType.AreaPlan,
        Autodesk.Revit.DB.ViewType.DraftingView,
        Autodesk.Revit.DB.ViewType.Legend,
        Autodesk.Revit.DB.ViewType.Schedule,
    };

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

        // Parameter-filtered: Revit matches the number natively instead of us walking every sheet.
        var rule = new FilterStringRule(
            new ParameterValueProvider(new ElementId(BuiltInParameter.SHEET_NUMBER)),
            new FilterStringEquals(), number!);
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .WherePasses(new ElementParameterFilter(rule))
            .Cast<ViewSheet>()
            .FirstOrDefault();
    }

    /// <summary>Loads only the in-scope views placed on <paramref name="sheet"/>.</summary>
    public static List<ViewItemViewModel> LoadForSheet(Document doc, ViewSheet sheet)
    {
        var viewIds = new HashSet<ElementId>();

        foreach (var vpId in sheet.GetAllViewports())
            if (doc.GetElement(vpId) is Viewport vp) viewIds.Add(vp.ViewId);

        foreach (var si in new FilteredElementCollector(doc, sheet.Id)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .Cast<ScheduleSheetInstance>())
            viewIds.Add(si.ScheduleId);

        // Legends/Schedules show only this sheet until "Load all views" fills in the rest.
        var onThisSheet = new List<string> { sheet.SheetNumber };
        var views = viewIds
            .Select(id => doc.GetElement(id) as View)
            .Where(v => v != null && IsInScope(v!))
            .Cast<View>();

        return Build(views, _ => onThisSheet);
    }

    /// <summary>Loads every in-scope view in the project with its full sheet placement.</summary>
    public static List<ViewItemViewModel> LoadAll(Document doc)
    {
        var placed = ViewClassificationService.BuildPlacedSheetsLookup(doc);
        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(IsInScope);

        return Build(views, v => placed.TryGetValue(v.Id, out var list) ? list : new List<string>());
    }

    private static bool IsInScope(View v) => !v.IsTemplate && InScopeViewTypes.Contains(v.ViewType);

    private static List<ViewItemViewModel> Build(
        IEnumerable<View> views, System.Func<View, List<string>> multiSheetPlacement)
    {
        var items = new List<ViewItemViewModel>();
        foreach (var v in views)
        {
            var (group, display) = ViewClassificationService.ClassifyView(v);

            IReadOnlyList<string> placedSheets;
            string? detailNumber = null;

            if (v.ViewType == Autodesk.Revit.DB.ViewType.Legend || v.ViewType == Autodesk.Revit.DB.ViewType.Schedule)
            {
                placedSheets = multiSheetPlacement(v);
            }
            else
            {
                var sheetNum = v.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER)?.AsString();
                detailNumber = v.get_Parameter(BuiltInParameter.VIEWER_DETAIL_NUMBER)?.AsString();
                placedSheets = string.IsNullOrWhiteSpace(sheetNum)
                    ? new List<string>()
                    : new List<string> { sheetNum! };
            }

            items.Add(new ViewItemViewModel(v, group, display, placedSheets, detailNumber));
        }
        return items;
    }
}
