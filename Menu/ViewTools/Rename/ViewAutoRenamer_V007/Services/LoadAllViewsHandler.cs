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

    /// <summary>Runs through ToolGuard so no exception reaches Revit.</summary>
    public void Execute(UIApplication app) =>
        ToolGuard.RunHandler(typeof(LoadAllViewsHandler), app, Run);

    private void Run(UIApplication app)
    {
        var vm = Vm;
        if (vm == null) return;

        List<ViewItemViewModel> items;
        try
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) throw new InvalidOperationException("No project is open.");
            items = ViewLoadService.LoadAll(doc);
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
