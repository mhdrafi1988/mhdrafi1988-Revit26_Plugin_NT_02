using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

/// <summary>Loads the rest of the project's views into an open window, on the Revit API thread.</summary>
public class LoadAllViewsHandler : IExternalEventHandler
{
    public ViewsListViewModel? Vm { get; set; }

    /// <summary>
    /// Runs through ToolGuard so no exception reaches Revit. Esc or the Cancel button asks
    /// "Cancel the process?"; Yes stops the load (a read-only run, so nothing needs undoing).
    /// </summary>
    public void Execute(UIApplication app)
    {
        var vm = Vm;
        ToolCancellation? cancel = null;
        ToolGuard.RunHandler(typeof(LoadAllViewsHandler), app, a =>
        {
            if (vm == null) return;
            cancel = vm.BeginRun(ToolCatalog.ViewAutoRenamerV007);
            try { Run(a, vm, cancel); }
            finally { vm.EndRun(cancel); }
        },
        onCancelled: () => vm?.OnLoadAllCancelled(cancel?.LastDone ?? 0, cancel?.LastTotal ?? 0));
    }

    private static void Run(UIApplication app, ViewsListViewModel vm, ToolCancellation cancel)
    {
        List<ViewItemViewModel> items;
        try
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) throw new InvalidOperationException("No project is open.");
            items = ViewLoadService.LoadAll(doc, cancel);
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

        System.Windows.Application.Current?.Dispatcher.Invoke(() => vm.MergeLoadedViews(items));
    }

    public string GetName() => "VAR02 Load All Views Handler";
}
