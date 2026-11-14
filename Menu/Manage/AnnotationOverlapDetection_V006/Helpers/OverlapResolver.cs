using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers
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
    }

    /// <summary>Planned moves for a whole resolve run.</summary>
    public sealed class ResolvePlan
    {
        /// <summary>Total move per element along the view's up direction, mm (positive = up).</summary>
        public Dictionary<long, double> MovesMm { get; } = new();

        /// <summary>One entry per requested pair, in request order.</summary>
        public List<PairPlan> Pairs { get; } = new();
    }

    /// <summary>
    /// Pure maths for resolving annotation clashes. No Revit API, so it is unit-tested on its own.
    /// Pairs are processed in order against a working copy of the boxes, so an element in several
    /// pairs is moved once per clash it still has, never twice for the same clash.
    /// </summary>
    public static class OverlapResolver
    {
        /// <summary>Largest clear gap accepted, mm.</summary>
        public const double MaxGapMm = 1000.0;

        /// <summary>
        /// Plans the moves that separate each pair vertically.
        /// </summary>
        /// <param name="boxes">Current box of every element named in <paramref name="pairs"/>, keyed by id.</param>
        /// <param name="pairs">Overlapping pairs (id1, id2) to resolve.</param>
        /// <param name="mode">Which element(s) move.</param>
        /// <param name="gapMm">Extra clearance left between the two boxes, mm (0 to <see cref="MaxGapMm"/>).</param>
        /// <param name="blockReason">Why an element cannot move (e.g. "pinned"), or null when it can. May be null.</param>
        /// <returns>The planned moves and a per-pair outcome.</returns>
        public static ResolvePlan Plan(
            IReadOnlyDictionary<long, ResolveBox> boxes,
            IEnumerable<(long id1, long id2)> pairs,
            ResolveMode mode,
            double gapMm,
            Func<long, string> blockReason = null,
            PriorityOptions priority = null)
        {
            if (mode == ResolveMode.ByPriority && priority == null)
                priority = new PriorityOptions();

            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            if (double.IsNaN(gapMm) || gapMm < 0 || gapMm > MaxGapMm)
                throw new ArgumentOutOfRangeException(nameof(gapMm), $"Gap must be between 0 and {MaxGapMm} mm.");

            // Working copy, updated as moves are planned.
            var work = new Dictionary<long, ResolveBox>();
            foreach (var kv in boxes)
                work[kv.Key] = new ResolveBox { Id = kv.Value.Id, X = kv.Value.X, Y = kv.Value.Y, Width = kv.Value.Width, Height = kv.Value.Height };

            var plan = new ResolvePlan();

            foreach (var (id1, id2) in pairs)
            {
                var entry = new PairPlan { Id1 = id1, Id2 = id2 };
                plan.Pairs.Add(entry);

                if (id1 == id2 || !work.TryGetValue(id1, out var a) || !work.TryGetValue(id2, out var b))
                {
                    entry.Outcome = PairOutcome.Skipped;
                    entry.Reason = "element no longer in the view";
                    continue;
                }

                if (!Intersect(a, b))
                {
                    entry.Outcome = PairOutcome.AlreadyClear;
                    continue;
                }

                // Upper = higher centre; on a tie the first element counts as upper.
                ResolveBox upper = a.CenterY >= b.CenterY ? a : b;
                ResolveBox lower = ReferenceEquals(upper, a) ? b : a;

                // Distance that puts the upper box's bottom gapMm above the lower box's top.
                double distance = lower.Top - upper.Y + gapMm;
                if (distance <= 0)
                {
                    entry.Outcome = PairOutcome.AlreadyClear;
                    continue;
                }

                (ResolveBox box, double delta)[] movers;
                if (mode == ResolveMode.ByPriority)
                {
                    movers = PriorityMovers(upper, lower, distance, priority, out string skip);
                    if (movers == null)
                    {
                        entry.Outcome = PairOutcome.Skipped;
                        entry.Reason = skip;
                        continue;
                    }
                }
                else
                {
                    movers = ModeMovers(mode, upper, lower, distance);
                }

                string blocked = null;
                foreach (var (box, _) in movers)
                {
                    string why = blockReason?.Invoke(box.Id);
                    if (!string.IsNullOrEmpty(why))
                    {
                        blocked = $"{box.Id} {why}";
                        break;
                    }
                }

                if (blocked != null)
                {
                    entry.Outcome = PairOutcome.Skipped;
                    entry.Reason = blocked;
                    continue;
                }

                foreach (var (box, delta) in movers)
                {
                    box.Y += delta;
                    plan.MovesMm[box.Id] = (plan.MovesMm.TryGetValue(box.Id, out var sum) ? sum : 0) + delta;
                    entry.MovedIds.Add(box.Id);
                }
                entry.Outcome = PairOutcome.Planned;
            }

            return plan;
        }

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
