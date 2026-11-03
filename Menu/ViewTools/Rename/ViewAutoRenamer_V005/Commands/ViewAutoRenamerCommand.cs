using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ViewAutoRenamer.V005.Services;
using Revit26_Plugin_Revised.ViewAutoRenamer.V005.ViewModels;
using Revit26_Plugin_Revised.ViewAutoRenamer.V005.Views;
using System.Diagnostics;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V005.Commands;

/// <summary>
/// Opens the window and nothing else.
///
/// This command used to build the whole dataset up front — a sheet-placement
/// table (two scoped collectors PER SHEET), every in-scope view in the model,
/// two get_Parameter calls per view, and a row view-model each — all on the
/// Revit UI thread before Show(). On a medium model that is seconds of frozen
/// UI before anything appears.
///
/// Now it does a cheap scan (IsTemplate + ViewType only) to populate the type
/// picker's counts, and the actual views are pulled on demand when the user
/// clicks Load. See LoadViewsHandler.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class OpenViewAutoRenamerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) =>
        ToolGuard.RunCommand(GetType(), c, ref m, e, ExecuteCore);

    private Result ExecuteCore(ExternalCommandData c, ref string m, ElementSet e)
    {
        try
        {
            return ExecuteInternal(c, ref m, e);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (System.Exception ex)
        {
            Logger.Error("OpenViewAutoRenamerCommand", ex);
            m = ex.Message;
            TaskDialog.Show("View Auto Renamer", $"An unexpected error occurred: {ex.Message}");
            return Result.Failed;
        }
    }

    private Result ExecuteInternal(ExternalCommandData c, ref string m, ElementSet e)
    {
        RevitEventManager.Initialize();

        var uidoc = c.Application.ActiveUIDocument;
        var doc   = uidoc.Document;

        string activeSheetNum = (uidoc.ActiveView as ViewSheet)?.SheetNumber ?? "";

        var sw         = Stopwatch.StartNew();
        var typeCounts = ViewClassificationService.ScanViewTypeCounts(doc);
        sw.Stop();

        var vm = new ViewsListViewModel(typeCounts, activeSheetNum, sw.ElapsedMilliseconds);
        new ViewsListWindow(vm).Show();

        return Result.Succeeded;
    }
}
