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

        /// <summary>
        /// Quick Align Ends / Bends to Tag: the user clicks one spot elevation, and the <paramref name="part"/>
        /// (leader end or bend point) of every selected spot slides onto an endless line through that tag's
        /// point. The line is vertical (same X) or horizontal (same Y) as last chosen in the window.
        /// </summary>
        public static Result RunToTag(ExternalCommandData commandData, SpotPart part)
        {
            AlignContext ctx = AlignRunner.Prepare(commandData);
            if (ctx == null) return Result.Cancelled;

            bool ends = part == SpotPart.LeaderEnd;
            if (ctx.Read.Items.Count(i => ends ? i.HasEnd : i.HasShoulder) == 0)
            {
                TaskDialog.Show(ctx.Title, ends
                    ? "None of the selected spot elevations has a leader, so there are no end points to align."
                    : "None of the selected spot elevations has a leader with a bend point, so there are no bend points to align.");
                return Result.Cancelled;
            }

            AlignOptions o = AlignSettingsStore.Load().Clone();
            AlignDirection direction = ends ? o.EndTagLineDirection : o.ShoulderTagLineDirection;
            // Esc throws OperationCanceledException, which ToolGuard turns into Cancelled.
            DetailLine2D line = AlignRunner.PickTagLine(ctx, part, direction, escThrows: true);
            if (line == null) return Result.Cancelled;

            o.Mode = ArrangeMode.AlignToDetailLines;
            o.TextLine = null;
            o.EndLine = ends ? line : null;
            o.ShoulderLine = ends ? null : line;
            o.EndLineMove = LineMove.Auto;
            o.ShoulderLineMove = LineMove.Auto;

            ArrangePlan plan = AlignEngine.Plan(ctx.Read.Items, o);
            string what = ends ? "end points" : "bend points";
            if (plan.MoveCount == 0)
            {
                TaskDialog.Show(ctx.Title, $"The selected {what} are already on that line.");
                return Result.Cancelled;
            }
            string where = direction == AlignDirection.Vertical ? "one column" : "one row";
            return AlignRunner.Apply(ctx, plan, $"aligned: {what} in {where} with {line.TagLabel}");
        }

        /// <summary>
        /// Quick Distribute: spaces the selected tags along their column (<paramref name="direction"/> =
        /// Vertical, up-down) or row (Horizontal, left-right) without lining them up, using the parts and
        /// spacing last saved by the window. "Keep current spacing" counts as even spacing. Even keeps the
        /// first and last tag; a fixed gap keeps the top / left tag.
        /// </summary>
        public static Result RunDistribute(ExternalCommandData commandData, AlignDirection direction)
        {
            AlignContext ctx = AlignRunner.Prepare(commandData);
            if (ctx == null) return Result.Cancelled;

            AlignOptions o = AlignSettingsStore.Load().Clone();
            o.Mode = ArrangeMode.DistributeOnly;
            o.Direction = direction;
            o.ReferenceId = null;
            if (o.Distribute == DistributeMode.None) o.Distribute = DistributeMode.Even;
            if (!o.MovesAnything) o.MoveText = true;

            if (o.Distribute == DistributeMode.Even && ctx.Read.Items.Count < 3)
            {
                TaskDialog.Show(ctx.Title, "Even spacing needs at least three spot elevations: the first and last stay " +
                    "and the others are spaced between them. Select more, or choose a fixed gap in the Align Spot Elevations window.");
                return Result.Cancelled;
            }

            ArrangePlan plan = AlignEngine.Plan(ctx.Read.Items, o);
            string where = direction == AlignDirection.Vertical ? "down their column" : "along their row";
            string spacing = o.Distribute == DistributeMode.FixedGap ? $"{o.GapMm:0.#} mm gap" : "even spacing";
            if (plan.MoveCount == 0)
            {
                TaskDialog.Show(ctx.Title, $"The selected spot elevations are already spaced {where} ({spacing}).");
                return Result.Cancelled;
            }
            var parts = new System.Collections.Generic.List<string>();
            if (o.MoveText) parts.Add("text");
            if (o.MoveLeaderEnd) parts.Add("leader ends");
            if (o.MoveLeaderShoulder) parts.Add("bend points");
            return AlignRunner.Apply(ctx, plan, $"spaced {where} ({string.Join(" + ", parts)}, {spacing})");
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
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitVertical, "Btn_AlignSpotElevations_QuickVertical_V001");
                    return QuickAlign.Run(d, AlignDirection.Vertical);
                });
    }

    /// <summary>Quick Align Horizontal: lines the selected spot elevation tags up in one row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitHorizontal, "Btn_AlignSpotElevations_QuickHorizontal_V001");
                    return QuickAlign.Run(d, AlignDirection.Horizontal);
                });
    }

    /// <summary>Quick Align Ends Vertical: lines the selected leader end points up in one column, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickEndsVerticalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitVertical, "Btn_AlignSpotElevations_QuickEndsVertical_V001");
                    return QuickAlign.Run(d, AlignDirection.Vertical, SpotPart.LeaderEnd);
                });
    }

    /// <summary>Quick Align Ends Horizontal: lines the selected leader end points up in one row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickEndsHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitHorizontal, "Btn_AlignSpotElevations_QuickEndsHorizontal_V001");
                    return QuickAlign.Run(d, AlignDirection.Horizontal, SpotPart.LeaderEnd);
                });
    }

    /// <summary>Quick Align Bends Vertical: lines the selected leader bend points up in one column, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickBendsVerticalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitVertical, "Btn_AlignSpotElevations_QuickBendsVertical_V001");
                    return QuickAlign.Run(d, AlignDirection.Vertical, SpotPart.LeaderShoulder);
                });
    }

    /// <summary>Quick Align Bends Horizontal: lines the selected leader bend points up in one row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickBendsHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitHorizontal, "Btn_AlignSpotElevations_QuickBendsHorizontal_V001");
                    return QuickAlign.Run(d, AlignDirection.Horizontal, SpotPart.LeaderShoulder);
                });
    }

    /// <summary>Quick Align Ends to Tag: lines the selected leader end points up with the end point of a clicked tag, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickEndsToTagCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitToTag, "Btn_AlignSpotElevations_QuickEndsToTag_V001");
                    return QuickAlign.RunToTag(d, SpotPart.LeaderEnd);
                });
    }

    /// <summary>Quick Align Bends to Tag: lines the selected leader bend points up with the bend point of a clicked tag, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickBendsToTagCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) =>
                {
                    QuickSplitMemory.Remember(QuickSplitMemory.SplitToTag, "Btn_AlignSpotElevations_QuickBendsToTag_V001");
                    return QuickAlign.RunToTag(d, SpotPart.LeaderShoulder);
                });
    }

    /// <summary>Quick Distribute Vertical: spaces the selected spot elevation tags up-down along their column, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickDistributeVerticalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.RunDistribute(d, AlignDirection.Vertical));
    }

    /// <summary>Quick Distribute Horizontal: spaces the selected spot elevation tags left-right along their row, no window.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AlignSpotElevationsQuickDistributeHorizontalCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => QuickAlign.RunDistribute(d, AlignDirection.Horizontal));
    }
}
