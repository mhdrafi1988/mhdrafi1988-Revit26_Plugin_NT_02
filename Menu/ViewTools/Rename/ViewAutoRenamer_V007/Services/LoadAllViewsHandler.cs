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

    /// <summary>True for the fast first load: only the active sheet's views, window stays in "partial" state.</summary>
    public bool CurrentSheetOnly { get; set; }

    /// <summary>View types to load; null loads every in-scope type.</summary>
    public ISet<Autodesk.Revit.DB.ViewType>? Types { get; set; }

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
            if (CurrentSheetOnly)
            {
                var uidoc = app.ActiveUIDocument;
                var sheet = ViewLoadService.FindCurrentSheet(doc, uidoc.ActiveView);
                items = sheet != null ? ViewLoadService.LoadForSheet(doc, sheet) : new List<ViewItemViewModel>();
                var number = sheet?.SheetNumber ?? "";
                System.Windows.Application.Current?.Dispatcher.Invoke(() => vm.OnSheetLoaded(items, number));
                return;
            }
            items = ViewLoadService.LoadAll(doc, Types);
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => vm.OnLoadAllFailed(ex.Message));
            throw; // ToolGuard logs it
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() => vm.MergeLoadedViews(items, Types));
    }

    public string GetName() => "VAR02 Load All Views Handler";
}
