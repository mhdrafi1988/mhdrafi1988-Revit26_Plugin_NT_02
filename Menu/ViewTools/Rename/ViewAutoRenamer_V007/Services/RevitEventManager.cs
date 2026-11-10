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

    public static void Initialize()
    {
        if (_handler != null) return;
        _handler = new RenameViewsHandler();
        _event   = ExternalEvent.Create(_handler);
        _loadHandler = new LoadAllViewsHandler();
        _loadEvent   = ExternalEvent.Create(_loadHandler);
    }

    public static void RequestRename(
        List<ViewItemViewModel> items,
        ViewsListViewModel vm)
    {
        _handler!.Payload = items;
        _handler.Vm       = vm;
        _event!.Raise();
    }

    /// <summary>
    /// Asks Revit to load views into <paramref name="vm"/>: every remaining view, or only the
    /// view-type groups in <paramref name="groups"/>.
    /// </summary>
    public static void RequestLoadAll(ViewsListViewModel vm, HashSet<Models.ViewTypeGroup>? groups = null)
    {
        _loadHandler!.Vm = vm;
        _loadHandler.InitialSheetLoad = false;
        _loadHandler.Groups = groups;
        _loadEvent!.Raise();
    }

    /// <summary>
    /// Asks Revit to load the current sheet's views into <paramref name="vm"/>. Raised by the
    /// command right after the window is shown, so the window appears before any view is read.
    /// </summary>
    public static void RequestInitialLoad(ViewsListViewModel vm)
    {
        _loadHandler!.Vm = vm;
        _loadHandler.InitialSheetLoad = true;
        _loadHandler.Groups = null;
        _loadEvent!.Raise();
    }
}
