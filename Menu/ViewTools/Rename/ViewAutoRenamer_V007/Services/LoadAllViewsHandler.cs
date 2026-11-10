using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

/// <summary>
/// Loads views into an open window, on the Revit API thread. Three kinds of request (7.4.0):
/// the current sheet's views right after the window opens, the whole project, or only some
/// view-type groups.
/// </summary>
public class LoadAllViewsHandler : IExternalEventHandler
{
    /// <summary>The window's view model that receives the views.</summary>
    public ViewsListViewModel? Vm { get; set; }

    /// <summary>True: load the views of the sheet the tool was started on (the first load after the window opens).</summary>
    public bool InitialSheetLoad { get; set; }

    /// <summary>View-type groups to load; null loads every group. Ignored for the first load.</summary>
    public HashSet<ViewTypeGroup>? Groups { get; set; }

    /// <summary>
    /// Runs through ToolGuard so no exception reaches Revit. Esc or the Cancel button asks
    /// "Cancel the process?"; Yes stops the load (a read-only run, so nothing needs undoing).
    /// </summary>
    public void Execute(UIApplication app)
    {
        var vm = Vm;
        bool initial = InitialSheetLoad;
        var groups = Groups;
        ToolCancellation? cancel = null;
        ToolGuard.RunHandler(typeof(LoadAllViewsHandler), app, a =>
        {
            if (vm == null) return;
            cancel = vm.BeginRun(ToolCatalog.ViewAutoRenamerV007);
            try { Run(a, vm, cancel, initial, groups); }
            finally { vm.EndRun(cancel); }
        },
        onCancelled: () => vm?.OnLoadAllCancelled(cancel?.LastDone ?? 0, cancel?.LastTotal ?? 0));
    }

    private static void Run(UIApplication app, ViewsListViewModel vm, ToolCancellation cancel,
        bool initial, HashSet<ViewTypeGroup>? groups)
    {
        List<ViewItemViewModel> items;
        string sheetNumber = "";
        var timer = System.Diagnostics.Stopwatch.StartNew(); // shown in the log, so a slow project can be reported with a number
        try
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) throw new InvalidOperationException("No project is open.");

            if (initial)
            {
                cancel.Report(0, 0, "Reading the current sheet's views…");
                var sheet = ViewLoadService.FindCurrentSheet(doc, uidoc!.ActiveView);
                sheetNumber = sheet?.SheetNumber ?? "";
                items = sheet != null ? ViewLoadService.LoadForSheet(doc, sheet) : new List<ViewItemViewModel>();
            }
            else
            {
                items = ViewLoadService.LoadAll(doc, cancel, groups);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // a user cancel is not a load failure: ToolGuard reports it as cancelled
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => vm.OnLoadAllFailed(ex.Message));
            throw; // ToolGuard logs it
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (initial) vm.SetInitialViews(items, sheetNumber, timer);
            else vm.MergeLoadedViews(items, timer, groups);
        });
    }

    public string GetName() => "VAR02 Load All Views Handler";
}
