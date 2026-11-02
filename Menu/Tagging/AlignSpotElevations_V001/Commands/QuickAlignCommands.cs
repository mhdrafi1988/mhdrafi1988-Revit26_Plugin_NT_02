using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Services;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands
{
    /// <summary>
    /// Quick align without the window: lines the selected tags up using the last options saved by the
    /// window (parts to move, line position, tolerance, spacing). Reference falls back to Average, since there is no
    /// window to choose the reference tag in.
    /// </summary>
    internal static class QuickAlign
    {
        /// <summary>
        /// Runs a quick align in <paramref name="direction"/>. With <paramref name="only"/> null it moves the
        /// parts last ticked in the window; otherwise only that part (the text follows a moved leader end or
        /// bend point, as in Revit).
        /// </summary>
        public static Result Run(ExternalCommandData commandData, AlignDirection direction, SpotPart? only = null)
        {
            AlignContext ctx = AlignRunner.Prepare(commandData);
            if (ctx == null) return Result.Cancelled;

            AlignOptions o = AlignSettingsStore.Load().Clone();
            o.Mode = ArrangeMode.AlignToLine;
            o.Direction = direction;
            o.ReferenceId = null;
            if (o.Target == AlignTarget.Reference) o.Target = AlignTarget.Average;
            if (!o.MovesAnything) o.MoveText = true;
            if (only.HasValue)
            {
                o.MoveText = only.Value == SpotPart.Text;
                o.MoveLeaderEnd = only.Value == SpotPart.LeaderEnd;
                o.MoveLeaderShoulder = only.Value == SpotPart.LeaderShoulder;
                int have = ctx.Read.Items.Count(i => only.Value == SpotPart.LeaderEnd ? i.HasEnd
                    : only.Value != SpotPart.LeaderShoulder || i.HasShoulder);
                if (have < 2)
                {
                    TaskDialog.Show(ctx.Title, only.Value == SpotPart.LeaderEnd
                        ? "Fewer than two of the selected spot elevations have a leader, so there are no end points to align."
                        : "Fewer than two of the selected spot elevations have a leader with a bend point, so there are no bend points to align.");
                    return Result.Cancelled;
                }
            }

            ArrangePlan plan = AlignEngine.Plan(ctx.Read.Items, o);
            string where = direction == AlignDirection.Vertical ? "one column" : "one row";
            if (plan.MoveCount == 0)
            {
                string what = only == SpotPart.LeaderEnd ? "leader ends" : only == SpotPart.LeaderShoulder ? "bend points" : "tags";
                TaskDialog.Show(ctx.Title, $"The selected {what} are already in {where}.");
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
            var parts = new System.Collections.Generic.List<string>();
            if (o.MoveText) parts.Add("text");
            if (o.MoveLeaderEnd) parts.Add("leader ends");
            if (o.MoveLeaderShoulder) parts.Add("bend points");
            return string.Join(" + ", parts) + ", " + target + spacing;
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

    /// <summary>Quick Align Ends Vertical: lines the selected leader end points up in one column, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickEndsVerticalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.Run(d, AlignDirection.Vertical, SpotPart.LeaderEnd));
    }

    /// <summary>Quick Align Ends Horizontal: lines the selected leader end points up in one row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickEndsHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.Run(d, AlignDirection.Horizontal, SpotPart.LeaderEnd));
    }

    /// <summary>Quick Align Bends Vertical: lines the selected leader bend points up in one column, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickBendsVerticalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.Run(d, AlignDirection.Vertical, SpotPart.LeaderShoulder));
    }

    /// <summary>Quick Align Bends Horizontal: lines the selected leader bend points up in one row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickBendsHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.Run(d, AlignDirection.Horizontal, SpotPart.LeaderShoulder));
    }
}
