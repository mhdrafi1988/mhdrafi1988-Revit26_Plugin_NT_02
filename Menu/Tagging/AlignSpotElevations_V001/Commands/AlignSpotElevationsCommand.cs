using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
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
    /// one at the same offset from its measured point. Can move the text, the leader end points and the
    /// leader bend points (shoulders), each on its own line. Remembers the last options.
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

            AlignOptions options = AlignSettingsStore.Load();
            if (ctx.PreselectedLine != null) options.Mode = ArrangeMode.AlignToDetailLines;
            if (StartInDetailLineMode) options.Mode = ArrangeMode.AlignToDetailLines;
            if (StartInDistributeMode) options.Mode = ArrangeMode.DistributeOnly;
            var vm = new AlignViewModel(ctx.Read.Items, ctx.Read.Skipped, ctx.Read.Scale, options);
            if (ctx.PreselectedLine != null) vm.SetLine(SpotPart.LeaderEnd, ctx.PreselectedLine);
            return ShowLoop(commandData, ctx, vm);
        }

        /// <summary>True for the Align to Detail Line button: the window opens in Align to detail lines.</summary>
        protected virtual bool StartInDetailLineMode => false;

        /// <summary>True for the Distribute button: the window opens in Distribute only.</summary>
        protected virtual bool StartInDistributeMode => false;

        /// <summary>
        /// Shows the window until Apply or Cancel. Pick line… / Pick tag… closes it; the user then clicks a
        /// detail line or a spot elevation in the view (no model change happens meanwhile) and the window
        /// opens again with the same options.
        /// </summary>
        internal static Result ShowLoop(ExternalCommandData commandData, AlignContext ctx, AlignViewModel vm)
        {
            while (true)
            {
                var window = new AlignWindow(vm);
                new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
                bool applied = window.ShowDialog() == true;
                if (vm.PendingPick.HasValue)
                {
                    vm.SetPickedLine(vm.PendingPickIsTag
                        ? AlignRunner.PickTagLine(ctx, vm.PendingPick.Value, vm.TagDirectionFor(vm.PendingPick.Value))
                        : AlignRunner.PickDetailLine(ctx, vm.PendingPick.Value));
                    continue;
                }
                if (!applied) return Result.Cancelled;

                AlignSettingsStore.Save(vm.Options);
                return AlignRunner.Apply(ctx, vm.Plan,
                    vm.IsDetailLineMode ? "aligned to the detail lines" : "arranged");
            }
        }
    }

    /// <summary>
    /// Align to Detail Line: opens Align Spot Elevations in the Align to detail lines mode. Pick a line (or,
    /// for the bend and end points, a tag) for the bend points, one for the leader end points and / or one
    /// for the text; each part slides onto its line vertically or horizontally where that is possible. A detail line selected with the spots is used
    /// for the end points.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsToDetailLineCommand : AlignSpotElevationsCommand
    {
        /// <inheritdoc/>
        protected override bool StartInDetailLineMode => true;
    }

    /// <summary>
    /// Distribute Spot Elevations: opens Align Spot Elevations in the Distribute only mode. The tags are
    /// spaced along their column or row (evenly, or at a fixed gap) and keep their other coordinate.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsDistributeCommand : AlignSpotElevationsCommand
    {
        /// <inheritdoc/>
        protected override bool StartInDistributeMode => true;
    }
}
