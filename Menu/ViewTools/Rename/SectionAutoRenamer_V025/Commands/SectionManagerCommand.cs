using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SectionAutoRenamer.V025.Services;
using Revit26_Plugin_Revised.SectionAutoRenamer.V025.ViewModels;
using Revit26_Plugin_Revised.SectionAutoRenamer.V025.Views;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SectionAutoRenamer.V025.Commands;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class OpenSectionManagerCommand : IExternalCommand
{
    /// <summary>Ribbon entry point; runs through ToolGuard so no exception reaches Revit.</summary>
    public Result Execute(ExternalCommandData c, ref string m, ElementSet e) =>
        ToolGuard.RunCommand(typeof(OpenSectionManagerCommand), c, ref m, e, ExecuteInternal);

    private static Result ExecuteInternal(ExternalCommandData c, ref string m, ElementSet e)
    {
        var uidoc = c.Application.ActiveUIDocument;
        if (uidoc?.Document == null)
        {
            m = "Open a project before running Section Auto Renamer.";
            TaskDialog.Show("Section Auto Renamer", m);
            return Result.Cancelled;
        }

        RevitEventManager.Initialize();
        var doc = uidoc.Document;

        var activeSheet       = uidoc.ActiveView as ViewSheet;
        string activeSheetNum = activeSheet?.SheetNumber ?? "";

        // Only collect true section views (not callouts — both are ViewSection subclasses)
        var sections = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSection))
            .Cast<ViewSection>()
            .Where(v => !v.IsTemplate && v.ViewType == ViewType.Section)
            .Select(v => new SectionItemViewModel(v))
            .ToList();

        var vm = new SectionsListViewModel(sections, activeSheetNum);
        new SectionsListWindow(vm).Show();

        return Result.Succeeded;
    }
}
