using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V007.Helpers
{
    /// <summary>How a clash between two annotations is resolved.</summary>
    public enum ResolveMode
    {
        /// <summary>The upper element of the pair moves up until the pair is clear.</summary>
        MoveUp = 0,

        /// <summary>The lower element of the pair moves down until the pair is clear.</summary>
        MoveDown = 1,

        /// <summary>Both elements move apart, half the distance each.</summary>
        Distribute = 2,

        /// <summary>
        /// V006: the lower-priority element moves away from the higher-priority one (up when it sits
        /// above, down when below). See <see cref="PriorityOptions"/>.
        /// </summary>
        ByPriority = 3
    }

    /// <summary>Settings for <see cref="ResolveMode.ByPriority"/>.</summary>
    public sealed class PriorityOptions
    {
        /// <summary>Rank of an element: 0 is the highest priority (stays), larger numbers move first.</summary>
        public Func<long, int> Rank { get; set; } = _ => 0;

        /// <summary>True when an element must never move (its category is marked "Never move").</summary>
        public Func<long, bool> NeverMove { get; set; } = _ => false;

        /// <summary>What happens when both elements have the same rank.</summary>
        public TieRule Tie { get; set; } = TieRule.Distribute;
    }

    /// <summary>
    /// An annotation's box in view coordinates (mm): <see cref="X"/> along the view's right direction,
    /// <see cref="Y"/> along its up direction, both at the box's lower-left corner.
    /// </summary>
    public sealed class ResolveBox
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Left edge along the view's right direction, mm.</summary>
        public double X { get; set; }

        /// <summary>Bottom edge along the view's up direction, mm.</summary>
        public double Y { get; set; }

        /// <summary>Width, mm.</summary>
        public double Width { get; set; }

        /// <summary>Height, mm.</summary>
        public double Height { get; set; }

        /// <summary>Right edge, mm.</summary>
        public double Right => X + Width;

        /// <summary>Top edge, mm.</summary>
        public double Top => Y + Height;

        /// <summary>Vertical centre, mm.</summary>
        public double CenterY => Y + Height / 2.0;
    }

    /// <summary>Outcome of one pair in a resolve plan.</summary>
    public enum PairOutcome
    {
        /// <summary>One or both elements are planned to move.</summary>
        Planned,

        /// <summary>The boxes no longer overlap (an earlier move cleared them).</summary>
        AlreadyClear,

        /// <summary>An element that has to move cannot be changed.</summary>
        Skipped
    }

    /// <summary>Planned result for one overlapping pair.</summary>
    public sealed class PairPlan
    {
        /// <summary>First element id of the pair, as listed in the grid.</summary>
        public long Id1 { get; set; }

        /// <summary>Second element id of the pair, as listed in the grid.</summary>
        public long Id2 { get; set; }

        /// <summary>What happens to this pair.</summary>
        public PairOutcome Outcome { get; set; }

        /// <summary>For <see cref="PairOutcome.Skipped"/>: the blocked element id and why, e.g. "412893 pinned".</summary>
        public string Reason { get; set; }

        /// <summary>For <see cref="PairOutcome.Planned"/>: ids of the element(s) that move for this pair (V006).</summary>
        public List<long> MovedIds { get; } = new();

        /// <summary>
        /// V007, with <see cref="ClearanceOptions.AvoidOthers"/>: ids of other annotations the moved
        /// element(s) were pushed past so they do not land on them. Empty otherwise.
        /// </summary>
        public List<long> PushedPast { get; } = new();
    }

    /// <summary>
    /// V007: how a resolve run treats the other annotations in the view. Null or
    /// <see cref="AvoidOthers"/> false gives the V006 behaviour (only the two boxes of a pair count).
    /// </summary>
    public sealed class ClearanceOptions
    {
        /// <summary>
        /// True: every box handed to <see cref="OverlapResolver.Plan"/> is an obstacle. A moved element
        /// is pushed further until it is clear of all of them, and the pairs are re-checked until stable.
        /// </summary>
        public bool AvoidOthers { get; set; }

        /// <summary>
        /// Least space kept between a moved element and any other annotation (not its partner, which
        /// uses the clear gap), mm. An annotation closer than this counts as in the way. 0 to
        /// <see cref="OverlapResolver.MaxGapMm"/>.
        /// </summary>
        public double NeighbourToleranceMm { get; set; }

        /// <summary>Most passes over the pairs before giving up on the ones still overlapping.</summary>
        public int MaxPasses { get; set; } = OverlapResolver.DefaultMaxPasses;

        /// <summary>
        /// Longest total move of one element in a run, mm (0 = no limit, up to
        /// <see cref="OverlapResolver.MaxMoveLimitMm"/>). A pair whose free spot is further away is skipped.
        /// </summary>
        public double MaxMoveMm { get; set; }
    }

    /// <summary>Planned moves for a whole resolve run.</summary>
    public sealed class ResolvePlan
    {
        /// <summary>Total move per element along the view's up direction, mm (positive = up).</summary>
        public Dictionary<long, double> MovesMm { get; } = new();

        /// <summary>One entry per requested pair, in request order.</summary>
        public List<PairPlan> Pairs { get; } = new();

        /// <summary>
        /// With <see cref="ClearanceOptions.AvoidOthers"/>: elements whose planned moves were taken back
        /// because a pair they belong to could not be separated (or because they would then land on an
        /// element that was taken back). They stay where they are. Value: why.
        /// </summary>
        public Dictionary<long, string> Undone { get; } = new();
    }

    /// <summary>
    /// Pure maths for resolving annotation clashes. No Revit API, so it is unit-tested on its own.
    /// Pairs are processed in order against a working copy of the boxes, so an element in several
    /// pairs is moved once per clash it still has, never twice for the same clash.
    /// V007: with <see cref="ClearanceOptions.AvoidOthers"/> every other box is an obstacle, a moved
    /// element is pushed on to the first free spot, and the pairs are re-checked until none changes.
    /// </summary>
    public static class OverlapResolver
    {
        /// <summary>Largest clear gap (and neighbour tolerance) accepted, mm.</summary>
        public const double MaxGapMm = 1000.0;

        /// <summary>Default number of passes over the pairs (V007).</summary>
        public const int DefaultMaxPasses = 10;

        /// <summary>Most obstacles one element may be pushed past in one move before it gives up.</summary>
        public const int MaxPushSteps = 500;

        /// <summary>Largest maximum move accepted, mm.</summary>
        public const double MaxMoveLimitMm = 100000.0;

        /// <summary>
        /// Plans the moves that separate each pair vertically.
        /// </summary>
        /// <param name="boxes">
        /// Current box of every element named in <paramref name="pairs"/>, keyed by id. With
        /// <see cref="ClearanceOptions.AvoidOthers"/> it may also hold every other annotation in the
        /// view; those never move but are kept clear of. Without it extra boxes are ignored.
        /// </param>
        /// <param name="pairs">Overlapping pairs (id1, id2) to resolve.</param>
        /// <param name="mode">Which element(s) move.</param>
        /// <param name="gapMm">Extra clearance left between the two boxes, mm (0 to <see cref="MaxGapMm"/>).</param>
        /// <param name="blockReason">Why an element cannot move (e.g. "pinned"), or null when it can. May be null.</param>
        /// <param name="priority">Settings for <see cref="ResolveMode.ByPriority"/>. May be null.</param>
        /// <param name="clearance">V007 neighbour handling. Null = V006 behaviour.</param>
        /// <returns>The planned moves and a per-pair outcome.</returns>
        public static ResolvePlan Plan(
            IReadOnlyDictionary<long, ResolveBox> boxes,
            IEnumerable<(long id1, long id2)> pairs,
            ResolveMode mode,
            double gapMm,
            Func<long, string> blockReason = null,
            PriorityOptions priority = null,
            ClearanceOptions clearance = null)
        {
            if (mode == ResolveMode.ByPriority && priority == null)
                priority = new PriorityOptions();

            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            if (double.IsNaN(gapMm) || gapMm < 0 || gapMm > MaxGapMm)
                throw new ArgumentOutOfRangeException(nameof(gapMm), $"Gap must be between 0 and {MaxGapMm} mm.");

            bool avoid = clearance != null && clearance.AvoidOthers;
            double tolerance = avoid ? clearance.NeighbourToleranceMm : 0;
            if (avoid && (double.IsNaN(tolerance) || tolerance < 0 || tolerance > MaxGapMm))
                throw new ArgumentOutOfRangeException(nameof(clearance), $"Neighbour tolerance must be between 0 and {MaxGapMm} mm.");
            double maxMove = avoid ? clearance.MaxMoveMm : 0;
            if (avoid && (double.IsNaN(maxMove) || maxMove < 0 || maxMove > MaxMoveLimitMm))
                throw new ArgumentOutOfRangeException(nameof(clearance), $"Maximum move must be between 0 and {MaxMoveLimitMm} mm.");

            // Working copy, updated as moves are planned.
            var work = new Dictionary<long, ResolveBox>();
            foreach (var kv in boxes)
                work[kv.Key] = new ResolveBox { Id = kv.Value.Id, X = kv.Value.X, Y = kv.Value.Y, Width = kv.Value.Width, Height = kv.Value.Height };

            var plan = new ResolvePlan();
            foreach (var (id1, id2) in pairs)
                plan.Pairs.Add(new PairPlan { Id1 = id1, Id2 = id2 });

            var ctx = new PlanContext
            {
                Work = work, Plan = plan, Mode = mode, GapMm = gapMm, BlockReason = blockReason,
                Priority = priority, Avoid = avoid, ToleranceMm = tolerance, MaxMoveMm = maxMove
            };

            // Pass 1 is the V006 algorithm (plus the free-spot search when avoiding others). Later
            // passes only run with AvoidOthers: a pair that a later move made overlap again is resolved
            // again, until a whole pass changes nothing.
            int passes = avoid ? Math.Max(1, clearance.MaxPasses) : 1;
            bool settled = false;
            for (int pass = 0; pass < passes; pass++)
            {
                bool changed = false;
                foreach (PairPlan entry in plan.Pairs)
                {
                    if (pass > 0 && entry.Outcome == PairOutcome.Skipped)
                        continue;
                    if (ResolvePair(ctx, entry, firstPass: pass == 0))
                        changed = true;
                }
                if (!changed)
                {
                    settled = true;
                    break;
                }
            }

            if (avoid && !settled)
            {
                foreach (PairPlan entry in plan.Pairs)
                {
                    if (entry.Outcome != PairOutcome.Skipped
                        && work.TryGetValue(entry.Id1, out var a) && work.TryGetValue(entry.Id2, out var b)
                        && Intersect(a, b))
                    {
                        entry.Outcome = PairOutcome.Skipped;
                        entry.Reason = $"still overlapping after {passes} passes";
                    }
                }
            }

            if (avoid)
                UndoUnresolved(work, plan, boxes);

            return plan;
        }

        /// <summary>
        /// A pair that ends skipped but still overlapping may have moved in earlier passes. Those moves
        /// fixed nothing, so they are taken back: the pair's moved elements return to where they were.
        /// Taking one back can leave another moved element on top of it (it moved into the gap the first
        /// one was going to leave); that one is taken back too, until nothing changes. Moves of pairs
        /// that did separate are kept.
        /// </summary>
        internal static void UndoUnresolved(Dictionary<long, ResolveBox> work, ResolvePlan plan,
            IReadOnlyDictionary<long, ResolveBox> original)
        {
            var undo = new Dictionary<long, string>();
            foreach (PairPlan entry in plan.Pairs)
            {
                if (entry.Outcome != PairOutcome.Skipped || entry.MovedIds.Count == 0)
                    continue;
                if (!work.TryGetValue(entry.Id1, out var a) || !work.TryGetValue(entry.Id2, out var b) || !Intersect(a, b))
                    continue;
                foreach (long id in entry.MovedIds)
                    if (!undo.ContainsKey(id))
                        undo[id] = $"pair {entry.Id1} / {entry.Id2} could not be separated";
            }
            if (undo.Count == 0)
                return;

            var reverted = new HashSet<long>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var kv in undo)
                {
                    if (!reverted.Add(kv.Key))
                        continue;
                    work[kv.Key].Y = original[kv.Key].Y;
                    plan.MovesMm.Remove(kv.Key);
                    plan.Undone[kv.Key] = kv.Value;
                }

                // A kept move that now overlaps something it did not overlap before the run goes too.
                foreach (long m in new List<long>(plan.MovesMm.Keys))
                {
                    ResolveBox mine = work[m];
                    foreach (ResolveBox o in work.Values)
                    {
                        if (o.Id == m || !Intersect(mine, o) || Intersect(original[m], original[o.Id]))
                            continue;
                        undo[m] = $"would land on {o.Id} once that stays put";
                        grew = true;
                        break;
                    }
                }
            }

            foreach (PairPlan entry in plan.Pairs)
            {
                long first = entry.MovedIds.Find(reverted.Contains);
                if (first == 0)
                    continue;
                entry.MovedIds.RemoveAll(reverted.Contains);
                bool overlaps = work.TryGetValue(entry.Id1, out var a) && work.TryGetValue(entry.Id2, out var b) && Intersect(a, b);
                if (entry.Outcome == PairOutcome.Skipped)
                    entry.Reason += " (its moves were undone)";
                else if (overlaps)
                {
                    entry.Outcome = PairOutcome.Skipped;
                    entry.Reason = $"not moved: {first} {plan.Undone[first]}";
                }
                else if (entry.MovedIds.Count == 0)
                    entry.Outcome = PairOutcome.AlreadyClear;
            }
        }

        private sealed class PlanContext
        {
            public Dictionary<long, ResolveBox> Work;
            public ResolvePlan Plan;
            public ResolveMode Mode;
            public double GapMm;
            public Func<long, string> BlockReason;
            public PriorityOptions Priority;
            public bool Avoid;
            public double ToleranceMm;
            public double MaxMoveMm;
        }

        /// <summary>
        /// Resolves one pair against the working boxes. On the first pass it always sets the pair's
        /// outcome; on later passes it only touches a pair that overlaps again. Returns true when
        /// something moved.
        /// </summary>
        private static bool ResolvePair(PlanContext ctx, PairPlan entry, bool firstPass)
        {
            long id1 = entry.Id1, id2 = entry.Id2;
            if (id1 == id2 || !ctx.Work.TryGetValue(id1, out var a) || !ctx.Work.TryGetValue(id2, out var b))
            {
                entry.Outcome = PairOutcome.Skipped;
                entry.Reason = "element no longer in the view";
                return false;
            }

            if (!Intersect(a, b))
            {
                if (firstPass)
                    entry.Outcome = PairOutcome.AlreadyClear;
                return false;
            }

            // Upper = higher centre; on a tie the first element counts as upper.
            ResolveBox upper = a.CenterY >= b.CenterY ? a : b;
            ResolveBox lower = ReferenceEquals(upper, a) ? b : a;

            // Distance that puts the upper box's bottom gapMm above the lower box's top.
            double distance = lower.Top - upper.Y + ctx.GapMm;
            if (distance <= 0)
            {
                if (firstPass)
                    entry.Outcome = PairOutcome.AlreadyClear;
                return false;
            }

            (ResolveBox box, double delta)[] movers;
            if (ctx.Mode == ResolveMode.ByPriority)
            {
                movers = PriorityMovers(upper, lower, distance, ctx.Priority, out string skip);
                if (movers == null)
                {
                    entry.Outcome = PairOutcome.Skipped;
                    entry.Reason = skip;
                    return false;
                }
            }
            else
            {
                movers = ModeMovers(ctx.Mode, upper, lower, distance);
            }

            foreach (var (box, _) in movers)
            {
                string why = ctx.BlockReason?.Invoke(box.Id);
                if (!string.IsNullOrEmpty(why))
                {
                    entry.Outcome = PairOutcome.Skipped;
                    entry.Reason = $"{box.Id} {why}";
                    return false;
                }
            }

            if (ctx.Avoid && !PushToFreeSpots(ctx, movers, a, b, entry))
                return false;

            foreach (var (box, delta) in movers)
            {
                box.Y += delta;
                ctx.Plan.MovesMm[box.Id] = (ctx.Plan.MovesMm.TryGetValue(box.Id, out var sum) ? sum : 0) + delta;
                if (!entry.MovedIds.Contains(box.Id))
                    entry.MovedIds.Add(box.Id);
            }
            entry.Outcome = PairOutcome.Planned;
            return true;
        }

        /// <summary>
        /// Lengthens each mover's move (same direction) until its box is clear of every other box by
        /// the neighbour tolerance. The partner is left out: moving further away only widens the gap
        /// to it. Movers are placed one after the other, so the second sees where the first will be.
        /// Returns false (pair skipped, nothing changed) when one of them finds no free spot.
        /// </summary>
        private static bool PushToFreeSpots(PlanContext ctx, (ResolveBox box, double delta)[] movers,
            ResolveBox a, ResolveBox b, PairPlan entry)
        {
            var originalY = new double[movers.Length];
            var pushed = new List<long>();
            for (int i = 0; i < movers.Length; i++)
            {
                var (box, delta) = movers[i];
                long partnerId = box.Id == a.Id ? b.Id : a.Id;
                double already = ctx.Plan.MovesMm.TryGetValue(box.Id, out var sum) ? sum : 0;
                double? free = FindFreeDelta(box, delta, partnerId, ctx.Work.Values, ctx.ToleranceMm, pushed,
                    ctx.MaxMoveMm, already);
                if (free == null)
                {
                    for (int k = 0; k < i; k++)
                        movers[k].box.Y = originalY[k];
                    entry.Outcome = PairOutcome.Skipped;
                    entry.Reason = ctx.MaxMoveMm > 0
                        ? $"no free space within {ctx.MaxMoveMm:0.##} mm for {box.Id}"
                        : $"no free space for {box.Id}";
                    return false;
                }

                // Place it now so the next mover avoids it; the caller applies the delta for real.
                originalY[i] = box.Y;
                box.Y += free.Value;
                movers[i] = (box, free.Value);
            }

            for (int i = 0; i < movers.Length; i++)
                movers[i].box.Y = originalY[i];
            foreach (long id in pushed)
                if (!entry.PushedPast.Contains(id))
                    entry.PushedPast.Add(id);
            return true;
        }

        /// <summary>
        /// Smallest move of <paramref name="box"/>, in the direction of <paramref name="delta"/> and at
        /// least as long, that leaves it at least <paramref name="toleranceMm"/> from every box in
        /// <paramref name="others"/> except itself and <paramref name="partnerId"/>. Null when no spot
        /// is found within <see cref="MaxPushSteps"/> pushes, or (with <paramref name="maxMoveMm"/> above 0)
        /// when the element's total move, <paramref name="alreadyMovedMm"/> included, would exceed it.
        /// </summary>
        public static double? FindFreeDelta(ResolveBox box, double delta, long partnerId,
            IEnumerable<ResolveBox> others, double toleranceMm, List<long> pushedPast = null,
            double maxMoveMm = 0, double alreadyMovedMm = 0)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));
            if (others == null) throw new ArgumentNullException(nameof(others));

            bool up = delta >= 0;
            double y = box.Y + delta;
            for (int step = 0; step < MaxPushSteps; step++)
            {
                // Pushes only go further in one direction, so once past the limit there is no spot.
                if (maxMoveMm > 0 && Math.Abs(alreadyMovedMm + (y - box.Y)) > maxMoveMm + 1e-9)
                    return null;

                // Every box in the way at this spot; jump past the furthest one in one go.
                ResolveBox far = null;
                foreach (ResolveBox o in others)
                {
                    if (o.Id == box.Id || o.Id == partnerId)
                        continue;
                    if (!Near(box.X, y, box.Width, box.Height, o, toleranceMm))
                        continue;
                    if (far == null || (up ? o.Top > far.Top : o.Y < far.Y))
                        far = o;
                }

                if (far == null)
                    return y - box.Y;

                pushedPast?.Add(far.Id);
                y = up ? far.Top + toleranceMm : far.Y - toleranceMm - box.Height;
            }
            return null;
        }

        /// <summary>
        /// True when a box at (<paramref name="x"/>, <paramref name="y"/>) of the given size is closer
        /// than <paramref name="toleranceMm"/> to <paramref name="o"/>, or overlaps it. With 0 this is
        /// the same test as <see cref="Intersect"/> (touching edges do not count).
        /// </summary>
        public static bool Near(double x, double y, double width, double height, ResolveBox o, double toleranceMm) =>
            x < o.Right + toleranceMm && o.X - toleranceMm < x + width &&
            y < o.Top + toleranceMm && o.Y - toleranceMm < y + height;

        private static (ResolveBox, double)[] ModeMovers(ResolveMode mode, ResolveBox upper, ResolveBox lower, double distance) => mode switch
        {
            ResolveMode.MoveUp => new[] { (upper, distance) },
            ResolveMode.MoveDown => new[] { (lower, -distance) },
            _ => new[] { (upper, distance / 2.0), (lower, -distance / 2.0) }
        };

        /// <summary>
        /// Who moves under <see cref="ResolveMode.ByPriority"/>. Returns null (and a reason) when the
        /// pair is left alone. Moving only one element still uses the full distance, so the result is
        /// the same clearance as Move Up / Move Down.
        /// </summary>
        private static (ResolveBox, double)[] PriorityMovers(ResolveBox upper, ResolveBox lower, double distance,
            PriorityOptions p, out string skipReason)
        {
            skipReason = null;
            bool upperFixed = p.NeverMove(upper.Id);
            bool lowerFixed = p.NeverMove(lower.Id);

            if (upperFixed && lowerFixed)
            {
                skipReason = "both are set to never move";
                return null;
            }
            if (upperFixed) return new[] { (lower, -distance) };
            if (lowerFixed) return new[] { (upper, distance) };

            int ru = p.Rank(upper.Id);
            int rl = p.Rank(lower.Id);
            if (ru > rl) return new[] { (upper, distance) };   // upper has lower priority: it moves up
            if (rl > ru) return new[] { (lower, -distance) };  // lower has lower priority: it moves down

            switch (p.Tie)
            {
                case TieRule.MoveUp: return new[] { (upper, distance) };
                case TieRule.MoveDown: return new[] { (lower, -distance) };
                case TieRule.Skip:
                    skipReason = "same priority";
                    return null;
                default: return new[] { (upper, distance / 2.0), (lower, -distance / 2.0) };
            }
        }

        /// <summary>True when the two boxes overlap with a positive area (touching edges do not count).</summary>
        public static bool Intersect(ResolveBox a, ResolveBox b) =>
            a.X < b.Right && b.X < a.Right && a.Y < b.Top && b.Y < a.Top;
    }
}
