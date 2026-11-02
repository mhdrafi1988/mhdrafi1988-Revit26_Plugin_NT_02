using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services
{
    /// <summary>
    /// Revit-free maths: chooses the line, spaces tags and plans every move. All values are paper mm.
    /// </summary>
    public static class AlignEngine
    {
        /// <summary>Box size used for a tag whose size is unknown, paper mm (two such tags closer than this overlap).</summary>
        public const double UnknownSizeMm = 1.0;

        /// <summary>Clearance added to the tag height (column) or width (row) by <see cref="SuggestGap"/>, paper mm.</summary>
        public const double GapClearanceMm = 1.0;

        /// <summary>Position of an item along the aligned axis (U for vertical, V for horizontal).</summary>
        public static double Axis(SpotItem item, AlignDirection direction)
            => direction == AlignDirection.Vertical ? item.U : item.V;

        /// <summary>Position of an item along the line (V for vertical, U for horizontal).</summary>
        public static double Along(SpotItem item, AlignDirection direction)
            => direction == AlignDirection.Vertical ? item.V : item.U;

        /// <summary>
        /// Chooses the common line position for <paramref name="items"/>.
        /// </summary>
        /// <param name="items">At least one tag.</param>
        /// <param name="direction">Axis to align.</param>
        /// <param name="target">How to choose.</param>
        /// <param name="tolerance">Positions closer than this count as equal (Most common).</param>
        /// <param name="reference">The reference tag (Reference target only).</param>
        public static TargetResult ChooseTarget(IReadOnlyList<SpotItem> items, AlignDirection direction,
            AlignTarget target, double tolerance, SpotItem reference = null)
        {
            if (items == null || items.Count == 0) throw new ArgumentException("No tags to align.", nameof(items));
            double[] v = items.Select(i => Axis(i, direction)).OrderBy(x => x).ToArray();
            double average = v.Average();

            switch (target)
            {
                case AlignTarget.Min: return new TargetResult { Value = v[0] };
                case AlignTarget.Max: return new TargetResult { Value = v[v.Length - 1] };
                case AlignTarget.Median:
                    return new TargetResult
                    {
                        Value = v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2.0
                    };
                case AlignTarget.Reference:
                    if (reference == null) throw new ArgumentException("Choose a reference tag.", nameof(reference));
                    return new TargetResult { Value = Axis(reference, direction) };
                case AlignTarget.MostCommon:
                    return MostCommon(v, average, Math.Max(0, tolerance));
                default:
                    return new TargetResult { Value = average };
            }
        }

        /// <summary>
        /// Finds the largest group of positions that all lie within <paramref name="tol"/> of the group's
        /// lowest position. Ties go to the group whose mean is nearest the overall average. A group of one
        /// means nothing is shared, so the average is used instead.
        /// </summary>
        private static TargetResult MostCommon(double[] sorted, double average, double tol)
        {
            int bestCount = 0;
            double bestMean = 0, bestDist = double.MaxValue;
            for (int i = 0; i < sorted.Length; i++)
            {
                int j = i;
                while (j + 1 < sorted.Length && sorted[j + 1] - sorted[i] <= tol) j++;
                int count = j - i + 1;
                double mean = 0;
                for (int k = i; k <= j; k++) mean += sorted[k];
                mean /= count;
                double dist = Math.Abs(mean - average);
                if (count > bestCount || (count == bestCount && dist < bestDist - 1e-12))
                {
                    bestCount = count; bestMean = mean; bestDist = dist;
                }
            }

            if (bestCount < 2)
                return new TargetResult { Value = average, FellBackToAverage = true, SharedCount = 1 };
            return new TargetResult { Value = bestMean, SharedCount = bestCount };
        }

        /// <summary>
        /// New positions along the line for <see cref="DistributeMode.Even"/> or
        /// <see cref="DistributeMode.FixedGap"/>, keyed by item. A column runs top to bottom (the top tag
        /// stays); a row runs left to right (the left tag stays). Equal positions keep their input order.
        /// </summary>
        public static Dictionary<SpotItem, double> Distribute(IReadOnlyList<SpotItem> items, AlignDirection direction,
            DistributeMode mode, double gapMm)
        {
            var result = new Dictionary<SpotItem, double>();
            if (items == null || items.Count == 0) return result;

            // Column: order top (high V) to bottom, step downwards. Row: order left to right, step right.
            bool column = direction == AlignDirection.Vertical;
            double sign = column ? -1 : 1;
            List<SpotItem> ordered = items
                .Select((item, index) => (item, index))
                .OrderBy(p => sign * Along(p.item, direction)).ThenBy(p => p.index)
                .Select(p => p.item).ToList();

            if (mode == DistributeMode.None)
            {
                foreach (SpotItem item in ordered) result[item] = Along(item, direction);
                return result;
            }

            double first = Along(ordered[0], direction);
            double step;
            if (mode == DistributeMode.FixedGap)
                step = sign * Math.Max(0, gapMm);
            else
                step = ordered.Count > 1 ? (Along(ordered[ordered.Count - 1], direction) - first) / (ordered.Count - 1) : 0;

            for (int i = 0; i < ordered.Count; i++) result[ordered[i]] = first + i * step;
            return result;
        }

        /// <summary>Leader ends closer than this to their line count as on it, paper mm.</summary>
        public const double ReachToleranceMm = 0.05;

        /// <summary>
        /// Plans the moves for <paramref name="options"/>. Returns one entry per tag, in input order.
        /// Each chosen part (text, leader end, bend point) is arranged on its own, using only the tags that
        /// have that part. A part that is not chosen follows the way Revit moves it: the end moves with the
        /// bend point, and the text moves with the end. With a bend, the end can only slide along the
        /// bend-to-end segment, so it may stop short of its line (<see cref="PlannedMove.EndLimited"/>).
        /// </summary>
        public static ArrangePlan Plan(IReadOnlyList<SpotItem> items, AlignOptions options)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var plan = new ArrangePlan();
            if (items.Count == 0) return plan;

            Dictionary<SpotItem, (double U, double V)> text = null, end = null, shoulder = null;
            if (options.MoveLeaderShoulder)
                shoulder = PlanPart(items, options, SpotPart.LeaderShoulder, plan, options.ShoulderOffsetXMm, options.ShoulderOffsetYMm);
            if (options.MoveLeaderEnd)
                end = PlanPart(items, options, SpotPart.LeaderEnd, plan, options.EndOffsetXMm, options.EndOffsetYMm);
            if (options.MoveText)
                text = PlanPart(items, options, SpotPart.Text, plan, options.OffsetXMm, options.OffsetYMm);
            if (options.MoveLeaderEnd) plan.MissingEndCount = items.Count(i => !i.HasEnd);
            if (options.MoveLeaderShoulder) plan.MissingShoulderCount = items.Count(i => !i.HasShoulder);
            plan.Target = plan.Targets.TryGetValue(SpotPart.Text, out TargetResult t) ? t
                : plan.Targets.Values.FirstOrDefault();

            var finalU = new double[items.Count];
            var finalV = new double[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                SpotItem it = items[i];
                var m = new PlannedMove { Item = it };

                // Bend point: chosen and present, else stays.
                double sdU = 0, sdV = 0;
                if (shoulder != null && shoulder.TryGetValue(it, out var s))
                {
                    sdU = s.U - it.ShoulderU; sdV = s.V - it.ShoulderV;
                }

                // Leader end: follows the bend point, or is set (projected onto the bend-to-end line).
                double edU = it.HasEnd ? sdU : 0, edV = it.HasEnd ? sdV : 0;
                bool setsEnd = false;
                if (end != null && end.TryGetValue(it, out var e))
                {
                    double tu = e.U, tv = e.V;
                    if (it.HasShoulder)
                    {
                        double du = it.EndU - it.ShoulderU, dv = it.EndV - it.ShoulderV;
                        double len = Math.Sqrt(du * du + dv * dv);
                        double su = it.ShoulderU + sdU, sv = it.ShoulderV + sdV;
                        if (len < 1e-9) { tu = it.EndU + sdU; tv = it.EndV + sdV; }
                        else
                        {
                            du /= len; dv /= len;
                            double along = (e.U - su) * du + (e.V - sv) * dv;
                            tu = su + du * along; tv = sv + dv * along;
                        }
                        if (Math.Abs(tu - e.U) > ReachToleranceMm || Math.Abs(tv - e.V) > ReachToleranceMm) m.EndLimited = true;
                    }
                    edU = tu - it.EndU; edV = tv - it.EndV;
                    setsEnd = true;
                }

                // Text: set, or follows the leader end.
                double tdU = edU, tdV = edV;
                bool setsText = false;
                if (text != null && text.TryGetValue(it, out var x))
                {
                    tdU = x.U - it.U; tdV = x.V - it.V;
                    setsText = true;
                }

                m.ShoulderDeltaU = Clean(sdU); m.ShoulderDeltaV = Clean(sdV);
                m.EndDeltaU = Clean(edU); m.EndDeltaV = Clean(edV);
                m.DeltaU = Clean(tdU); m.DeltaV = Clean(tdV);
                m.SetsShoulder = shoulder != null && it.HasShoulder && (m.ShoulderDeltaU != 0 || m.ShoulderDeltaV != 0);
                m.SetsEnd = setsEnd && (m.EndDeltaU != 0 || m.EndDeltaV != 0);
                m.SetsText = setsText && (m.DeltaU != 0 || m.DeltaV != 0);
                plan.Entries.Add(m);
                finalU[i] = it.U + m.DeltaU;
                finalV[i] = it.V + m.DeltaV;
            }

            bool[] overlaps = FindOverlaps(items, finalU, finalV);
            for (int i = 0; i < items.Count; i++) plan.Entries[i].Overlaps = overlaps[i];
            plan.OverlapCount = overlaps.Count(o => o);
            plan.EndLimitedCount = plan.Entries.Count(m => m.EndLimited);
            return plan;
        }

        /// <summary>
        /// Arranges one part of the tags that have it. Returns the new position of that part, keyed by tag.
        /// </summary>
        private static Dictionary<SpotItem, (double U, double V)> PlanPart(IReadOnlyList<SpotItem> items,
            AlignOptions options, SpotPart part, ArrangePlan plan, double offsetX, double offsetY)
        {
            var result = new Dictionary<SpotItem, (double, double)>();
            // Stand-ins whose U/V are the part's position, so the line maths works on any part.
            var proxies = new List<SpotItem>();
            var owner = new Dictionary<SpotItem, SpotItem>();
            foreach (SpotItem it in items)
            {
                if (part == SpotPart.LeaderEnd && !it.HasEnd) continue;
                if (part == SpotPart.LeaderShoulder && !it.HasShoulder) continue;
                var p = new SpotItem
                {
                    Id = it.Id, Label = it.Label, PointU = it.PointU, PointV = it.PointV,
                    U = part == SpotPart.LeaderEnd ? it.EndU : part == SpotPart.LeaderShoulder ? it.ShoulderU : it.U,
                    V = part == SpotPart.LeaderEnd ? it.EndV : part == SpotPart.LeaderShoulder ? it.ShoulderV : it.V
                };
                proxies.Add(p);
                owner[p] = it;
            }
            if (proxies.Count == 0) return result;

            AlignDirection dir = options.Direction;
            if (options.Mode == ArrangeMode.OffsetFromPoint)
            {
                foreach (SpotItem p in proxies) result[owner[p]] = (p.PointU + offsetX, p.PointV + offsetY);
                return result;
            }

            double? line = null;
            if (options.Mode == ArrangeMode.AlignToLine)
            {
                SpotItem reference = options.ReferenceId.HasValue
                    ? proxies.FirstOrDefault(p => p.Id == options.ReferenceId.Value) : null;
                bool missing = false;
                if (options.Target == AlignTarget.Reference && reference == null)
                {
                    missing = options.ReferenceId.HasValue && items.Any(i => i.Id == options.ReferenceId.Value);
                    reference = proxies[0];
                }
                TargetResult target = ChooseTarget(proxies, dir, options.Target, options.ToleranceMm, reference);
                target.ReferenceMissing = missing;
                plan.Targets[part] = target;
                line = target.Value;
            }

            DistributeMode spacing = options.Mode == ArrangeMode.DistributeOnly && options.Distribute == DistributeMode.None
                ? DistributeMode.Even : options.Distribute;
            Dictionary<SpotItem, double> along = Distribute(proxies, dir, spacing, options.GapMm);
            foreach (SpotItem p in proxies)
            {
                double axis = line ?? Axis(p, dir);
                double a = along[p];
                result[owner[p]] = dir == AlignDirection.Vertical ? (axis, a) : (a, axis);
            }
            return result;
        }

        /// <summary>
        /// Marks every tag whose box, centred on its (new) position, overlaps another tag's box. Boxes use the
        /// tags' estimated sizes; a tag of unknown size counts as <see cref="UnknownSizeMm"/> square.
        /// </summary>
        public static bool[] FindOverlaps(IReadOnlyList<SpotItem> items, IReadOnlyList<double> u, IReadOnlyList<double> v)
        {
            var hit = new bool[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    double w = (Size(items[i].WidthMm) + Size(items[j].WidthMm)) / 2;
                    double h = (Size(items[i].HeightMm) + Size(items[j].HeightMm)) / 2;
                    if (Math.Abs(u[i] - u[j]) < w - 1e-9 && Math.Abs(v[i] - v[j]) < h - 1e-9) hit[i] = hit[j] = true;
                }
            }
            return hit;
        }

        /// <summary>
        /// A fixed gap that keeps tags apart: the tallest tag plus <see cref="GapClearanceMm"/> for a column,
        /// the widest for a row, rounded up to 0.5 mm. Null when no tag size is known.
        /// </summary>
        public static double? SuggestGap(IReadOnlyList<SpotItem> items, AlignDirection direction)
        {
            if (items == null || items.Count == 0) return null;
            double max = items.Max(i => direction == AlignDirection.Vertical ? i.HeightMm : i.WidthMm);
            if (max <= 0) return null;
            return Math.Ceiling((max + GapClearanceMm) * 2) / 2;
        }

        private static double Size(double mm) => mm > 0 ? mm : UnknownSizeMm;

        private static double Clean(double d) => Math.Abs(d) < 1e-6 ? 0 : d;
    }
}
