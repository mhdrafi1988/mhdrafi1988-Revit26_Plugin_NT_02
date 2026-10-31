using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands
{
    /// <summary>
    /// Quick align without the window: lines the selected tags up using the last options saved by the
    /// window (line position, tolerance, spacing). Reference falls back to Average, since there is no
    /// window to choose the reference tag in.
    /// </summary>
    internal static class QuickAlign
    {
        /// <summary>Runs a quick align in <paramref name="direction"/>.</summary>
        public static Result Run(ExternalCommandData commandData, AlignDirection direction)
        {
            AlignContext ctx = AlignRunner.Prepare(commandData);
            if (ctx == null) return Result.Cancelled;

            AlignOptions o = AlignSettingsStore.Load().Clone();
            o.Mode = ArrangeMode.AlignToLine;
            o.Direction = direction;
            o.ReferenceId = null;
            if (o.Target == AlignTarget.Reference) o.Target = AlignTarget.Average;

            ArrangePlan plan = AlignEngine.Plan(ctx.Read.Items, o);
            string where = direction == AlignDirection.Vertical ? "one column" : "one row";
            if (plan.MoveCount == 0)
            {
                TaskDialog.Show(ctx.Title, $"The selected tags are already in {where}.");
                return Result.Cancelled;
            }
            return AlignRunner.Apply(ctx, plan, $"aligned in {where} ({Describe(o)})");
        }

        private static string Describe(AlignOptions o)
        {
            string target = o.Target switch
            {
                AlignTarget.MostCommon => $"most common, within {o.ToleranceMm:0.#} mm",
                AlignTarget.Median => "median",
                AlignTarget.Min => "min",
                AlignTarget.Max => "max",
                _ => "average"
            };
            string spacing = o.Distribute switch
            {
                DistributeMode.Even => ", even spacing",
                DistributeMode.FixedGap => $", {o.GapMm:0.#} mm gap",
                _ => string.Empty
            };
            return target + spacing;
        }
    }

    /// <summary>Quick Align Vertical: lines the selected spot elevation tags up in one column, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickVerticalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.Run(d, AlignDirection.Vertical));
    }

    /// <summary>Quick Align Horizontal: lines the selected spot elevation tags up in one row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.Run(d, AlignDirection.Horizontal));
    }
}
