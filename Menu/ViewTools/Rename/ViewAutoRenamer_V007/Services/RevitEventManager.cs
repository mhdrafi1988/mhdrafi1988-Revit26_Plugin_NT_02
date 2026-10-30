using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

public static class RevitEventManager
{
    private static RenameViewsHandler? _handler;
    private static ExternalEvent?      _event;
    private static LoadAllViewsHandler? _loadHandler;
    private static ExternalEvent?      _loadEvent;
    private static LoadAllViewsHandler? _sheetHandler;
    private static ExternalEvent?      _sheetEvent;

    public static void Initialize()
    {
        if (_handler != null) return;
        _handler = new RenameViewsHandler();
        _event   = ExternalEvent.Create(_handler);
        _loadHandler = new LoadAllViewsHandler();
        _loadEvent   = ExternalEvent.Create(_loadHandler);
        _sheetHandler = new LoadAllViewsHandler { CurrentSheetOnly = true };
        _sheetEvent   = ExternalEvent.Create(_sheetHandler);
    }

    public static void RequestRename(
        List<ViewItemViewModel> items,
        ViewsListViewModel vm)
    {
        _handler!.Payload = items;
        _handler.Vm       = vm;
        _event!.Raise();
    }

    /// <summary>Asks Revit to load every remaining view into <paramref name="vm"/>.</summary>
    public static void RequestLoadAll(ViewsListViewModel vm)
    {
        _loadHandler!.Vm = vm;
        _loadEvent!.Raise();
    }

    /// <summary>Asks Revit to load the active sheet's views into <paramref name="vm"/> (fast first open).</summary>
    public static void RequestLoadCurrentSheet(ViewsListViewModel vm)
    {
        _sheetHandler!.Vm = vm;
        _sheetEvent!.Raise();
    }
}
