using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Views;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Commands;

/// <summary>
/// Opens View Auto Renamer V007. The window is shown first; the current sheet's views are read
/// right after, and the rest of the project is loaded on demand from the window.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class OpenViewAutoRenamerCommand : IExternalCommand
{
    /// <summary>Ribbon entry point; runs through ToolGuard so no exception reaches Revit.</summary>
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) =>
        ToolGuard.RunCommand(typeof(OpenViewAutoRenamerCommand), c, ref m, e, ExecuteInternal);

    private static Result ExecuteInternal(ExternalCommandData c, ref string m, ElementSet e)
    {
        var uidoc = c.Application.ActiveUIDocument;
        if (uidoc?.Document == null)
        {
            m = "Open a project before running View Auto Renamer.";
            return Result.Failed;
        }
        var doc = uidoc.Document;

        RevitEventManager.Initialize();

        // 7.4.0: show the window first, then read the current sheet's views in an external
        // event, so the window appears at once. The rest of the project loads on request
        // ("Load all views" / "Load by type").
        var vm = new ViewsListViewModel(new List<ViewItemViewModel>(), "", allViewsLoaded: false, initialLoadPending: true);
        new ViewsListWindow(vm).Show();
        RevitEventManager.RequestInitialLoad(vm);

        return Result.Succeeded;
    }
}
