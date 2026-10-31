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
        /// <summary>Two tags closer than this on both axes count as stacked, paper mm.</summary>
        public const double StackDistanceMm = 1.0;

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

        /// <summary>
        /// Plans the moves for <paramref name="options"/>. Returns one entry per tag, in input order.
        /// </summary>
        public static ArrangePlan Plan(IReadOnlyList<SpotItem> items, AlignOptions options)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var plan = new ArrangePlan();
            if (items.Count == 0) return plan;

            var finalU = new double[items.Count];
            var finalV = new double[items.Count];
            AlignDirection dir = options.Direction;

            if (options.Mode == ArrangeMode.OffsetFromPoint)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    finalU[i] = items[i].PointU + options.OffsetXMm;
                    finalV[i] = items[i].PointV + options.OffsetYMm;
                }
            }
            else
            {
                double? line = null;
                if (options.Mode == ArrangeMode.AlignToLine)
                {
                    SpotItem reference = options.ReferenceId.HasValue
                        ? items.FirstOrDefault(i => i.Id == options.ReferenceId.Value) : null;
                    if (options.Target == AlignTarget.Reference && reference == null) reference = items[0];
                    plan.Target = ChooseTarget(items, dir, options.Target, options.ToleranceMm, reference);
                    line = plan.Target.Value;
                }

                DistributeMode spacing = options.Mode == ArrangeMode.DistributeOnly && options.Distribute == DistributeMode.None
                    ? DistributeMode.Even : options.Distribute;
                Dictionary<SpotItem, double> along = Distribute(items, dir, spacing, options.GapMm);

                for (int i = 0; i < items.Count; i++)
                {
                    double axis = line ?? Axis(items[i], dir);
                    double a = along[items[i]];
                    finalU[i] = dir == AlignDirection.Vertical ? axis : a;
                    finalV[i] = dir == AlignDirection.Vertical ? a : axis;
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                plan.Entries.Add(new PlannedMove
                {
                    Item = items[i],
                    DeltaU = Clean(finalU[i] - items[i].U),
                    DeltaV = Clean(finalV[i] - items[i].V)
                });
            }
            plan.StackedCount = CountStacked(finalU, finalV, StackDistanceMm);
            return plan;
        }

        /// <summary>Number of positions that lie within <paramref name="distance"/> of another on both axes.</summary>
        public static int CountStacked(IReadOnlyList<double> u, IReadOnlyList<double> v, double distance)
        {
            int n = 0;
            for (int i = 0; i < u.Count; i++)
            {
                for (int j = 0; j < u.Count; j++)
                {
                    if (i == j) continue;
                    if (Math.Abs(u[i] - u[j]) < distance && Math.Abs(v[i] - v[j]) < distance) { n++; break; }
                }
            }
            return n;
        }

        private static double Clean(double d) => Math.Abs(d) < 1e-6 ? 0 : d;
    }
}
