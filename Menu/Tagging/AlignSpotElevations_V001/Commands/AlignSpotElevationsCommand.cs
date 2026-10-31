using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.ViewModels;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands
{
    /// <summary>
    /// Arranges the tags of the selected spot elevations: lines them up in a column or row (average, most
    /// common, median, min / max or a reference tag), spaces them evenly or at a fixed gap, or places each
    /// one at the same offset from its measured point. Remembers the last options.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            AlignContext ctx = AlignRunner.Prepare(commandData);
            if (ctx == null) return Result.Cancelled;

            var vm = new AlignViewModel(ctx.Read.Items, ctx.Read.Skipped, ctx.Read.Scale, AlignSettingsStore.Load());
            var window = new AlignWindow(vm);
            new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
            if (window.ShowDialog() != true) return Result.Cancelled;

            AlignSettingsStore.Save(vm.Options);
            return AlignRunner.Apply(ctx, vm.Plan, "arranged");
        }
    }
}
