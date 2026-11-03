using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V005.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V005.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V005.Services;

/// <summary>
/// Loads the views the user asked for, on demand.
///
/// Runs through an ExternalEvent rather than being called straight from the
/// Load button: the window is modeless, so reading the document from a WPF
/// click handler is outside Revit's permitted context and can throw. Same
/// pattern as RenameViewsHandler (see Claude/ARCHITECTURE.md §2).
///
/// Work order matters for cost — placement lookup first, then the type
/// filter, then the placed/sheet criteria, and only THEN read view.Name and
/// build row view-models. Everything filtered out is never materialised.
/// </summary>
public class LoadViewsHandler : IExternalEventHandler
{
    public ViewLoadCriteria  Criteria { get; set; } = new();
    public ViewsListViewModel Vm      { get; set; } = null!;

    public void Execute(UIApplication app) =>
        ToolGuard.RunHandler(GetType(), app, ExecuteCore);

    private void ExecuteCore(UIApplication app)
    {
        var vm = Vm;
        if (vm == null) return;

        try
        {
            var sw  = Stopwatch.StartNew();
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
            {
                Dispatch(() => vm.ReportLoadFailed("No active document — nothing to load."));
                return;
            }

            var criteria = Criteria;
            if (!criteria.HasAnyType)
            {
                Dispatch(() => vm.ReportLoadFailed("No view types selected — tick at least one type, then Load."));
                return;
            }

            // One placement sweep for the whole document (3 collectors).
            var placementByViewId = ViewClassificationService.BuildPlacementLookup(doc);

            bool wantSheetText = !string.IsNullOrWhiteSpace(criteria.SheetNumberContains);
            var  sheetText     = criteria.SheetNumberContains?.Trim() ?? "";

            var items = new List<ViewItemViewModel>();

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .WhereElementIsNotElementType();

            foreach (var e in collector)
            {
                if (e is not View v || v.IsTemplate) continue;

                // Cheapest test first — ViewType is a direct property read.
                if (!criteria.ViewTypes.Contains(v.ViewType)) continue;
                if (!ViewClassificationService.TryClassify(v.ViewType, out var group, out var display)) continue;

                placementByViewId.TryGetValue(v.Id, out var placement);

                var sheetNumbers = placement?.SheetNumbers ?? EmptySheets;
                bool isPlaced    = sheetNumbers.Count > 0;

                if (isPlaced  && !criteria.IncludePlaced)    continue;
                if (!isPlaced && !criteria.IncludeNotPlaced) continue;

                if (wantSheetText)
                {
                    bool anyMatch = false;
                    foreach (var s in sheetNumbers)
                    {
                        if (s.IndexOf(sheetText, StringComparison.OrdinalIgnoreCase) >= 0) { anyMatch = true; break; }
                    }
                    if (!anyMatch) continue;
                }

                // Survived every filter — only now is it worth reading Name
                // (an API call) and allocating a row.
                items.Add(new ViewItemViewModel(
                    v, group, display,
                    sheetNumbers.Count > 0 ? sheetNumbers.ToArray() : EmptySheetArray,
                    placement?.DetailNumber));
            }

            sw.Stop();
            var elapsed = sw.ElapsedMilliseconds;
            Dispatch(() => vm.ApplyLoadedViews(items, elapsed));
        }
        catch (Exception ex)
        {
            Dispatch(() => vm.ReportLoadFailed($"Load failed: {ex.Message}"));
        }
    }

    private static readonly List<string>  EmptySheets     = new();
    private static readonly string[]      EmptySheetArray = Array.Empty<string>();

    /// <summary>
    /// Marshal back to the UI thread. Falls back to a direct call when there
    /// is no WPF Application or we are already on its thread — a silently
    /// dropped callback here would strand the window in its "Loading…" state.
    /// </summary>
    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.Invoke(action);
        else
            action();
    }

    public string GetName() => "VAR05 Load Views Handler";
}
