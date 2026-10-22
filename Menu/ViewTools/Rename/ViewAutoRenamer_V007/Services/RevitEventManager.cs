using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;

public static class RevitEventManager
{
    private static RenameViewsHandler? _handler;
    private static ExternalEvent?      _event;

    public static void Initialize()
    {
        if (_handler != null) return;
        _handler = new RenameViewsHandler();
        _event   = ExternalEvent.Create(_handler);
    }

    public static void RequestRename(
        List<ViewItemViewModel> items,
        ViewsListViewModel vm)
    {
        _handler!.Payload = items;
        _handler.Vm       = vm;
        _event!.Raise();
    }
}
