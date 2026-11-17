using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout
{
    /// <summary>Which edge of a track's members is made to share one line.</summary>
    public enum EdgeChoice
    {
        /// <summary>The low edge — bottom for a row, left for a column.</summary>
        Low,

        /// <summary>The centre line.</summary>
        Center,

        /// <summary>The high edge — top for a row, right for a column.</summary>
        High
    }

    /// <summary>
    /// The one-dimensional maths behind both axes: a row aligning its bottom edges and a column
    /// aligning its left edges are the same problem. Revit-free and unit tested
    /// (<c>Tests/SheetAlignDistribute.Tests</c>).
    /// </summary>
    public static class AxisAlign
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// The middle value of <paramref name="values"/>; the mean of the middle two when there is
        /// an even count. Throws for an empty list.
        /// </summary>
        /// <remarks>
        /// The shared line is a median rather than a mean on purpose. With a mean, one badly placed
        /// view drags its whole row toward itself, so every view in the row needs a move and the
        /// Max move limit trips for all of them. With a median the row holds still and only the
        /// outlier is out of line, which is what the limit is there to catch.
        /// </remarks>
        public static double Median(IEnumerable<double> values)
        {
            var sorted = values?.OrderBy(v => v).ToList() ?? throw new ArgumentNullException(nameof(values));
            if (sorted.Count == 0) throw new ArgumentException("No values to take the median of.", nameof(values));
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        /// <summary>
        /// The shared line for one track, clamped so that no member leaves
        /// <paramref name="availMin"/>..<paramref name="availMax"/>.
        /// </summary>
        /// <param name="lowEdges">Each member's current low edge (bottom, or left).</param>
        /// <param name="sizes">Each member's size on this axis (height, or width).</param>
        /// <param name="choice">Which edge shares the line.</param>
        /// <param name="availMin">Low end of the usable area on this axis.</param>
        /// <param name="availMax">High end of the usable area on this axis.</param>
        /// <param name="line">The shared line, in the terms <paramref name="choice"/> names.</param>
        /// <returns>False when the largest member cannot fit the usable area at all; the caller then leaves the track alone.</returns>
        public static bool SharedLine(IReadOnlyList<double> lowEdges, IReadOnlyList<double> sizes,
            EdgeChoice choice, double availMin, double availMax, out double line)
        {
            line = 0;
            if (lowEdges == null) throw new ArgumentNullException(nameof(lowEdges));
            if (sizes == null) throw new ArgumentNullException(nameof(sizes));
            if (lowEdges.Count == 0 || lowEdges.Count != sizes.Count) return false;

            double largest = sizes.Max();
            if (largest > availMax - availMin + Eps) return false;

            double raw = choice switch
            {
                EdgeChoice.Low => Median(lowEdges),
                EdgeChoice.Center => Median(lowEdges.Select((low, i) => low + sizes[i] / 2.0)),
                _ => Median(lowEdges.Select((low, i) => low + sizes[i]))
            };

            // The window the line may sit in, so that even the largest member stays inside the area.
            (double lo, double hi) = choice switch
            {
                EdgeChoice.Low => (availMin, availMax - largest),
                EdgeChoice.Center => (availMin + largest / 2.0, availMax - largest / 2.0),
                _ => (availMin + largest, availMax)
            };

            line = Math.Clamp(raw, lo, Math.Max(lo, hi));
            return true;
        }

        /// <summary>The new low edge of a member of <paramref name="size"/> sitting on <paramref name="line"/>.</summary>
        public static double LowEdgeFor(double line, double size, EdgeChoice choice) => choice switch
        {
            EdgeChoice.Low => line,
            EdgeChoice.Center => line - size / 2.0,
            _ => line - size
        };

        /// <summary>
        /// Picks the edge that needs the least total travel for this track, used by the
        /// "Least movement" alignment. Edges that do not fit the usable area are skipped; returns
        /// false when none fits.
        /// </summary>
        public static bool LeastMovementEdge(IReadOnlyList<double> lowEdges, IReadOnlyList<double> sizes,
            double availMin, double availMax, out EdgeChoice best, out double line)
        {
            best = EdgeChoice.Low;
            line = 0;
            bool found = false;
            double bestTravel = double.MaxValue;

            foreach (EdgeChoice choice in new[] { EdgeChoice.Low, EdgeChoice.Center, EdgeChoice.High })
            {
                if (!SharedLine(lowEdges, sizes, choice, availMin, availMax, out double candidate))
                    continue;

                double travel = 0;
                for (int i = 0; i < lowEdges.Count; i++)
                    travel += Math.Abs(LowEdgeFor(candidate, sizes[i], choice) - lowEdges[i]);

                if (travel < bestTravel - Eps)
                {
                    bestTravel = travel;
                    best = choice;
                    line = candidate;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Lays <paramref name="sizes"/> out along <paramref name="spanMin"/>..<paramref name="spanMax"/>,
        /// keeping at least <paramref name="minGaps"/> between consecutive members and sharing whatever
        /// room is left over equally between the gaps.
        /// </summary>
        /// <param name="sizes">Member sizes in the order they are laid out.</param>
        /// <param name="minGaps">The smallest gap allowed after each member; one entry fewer than <paramref name="sizes"/>.</param>
        /// <returns>
        /// Each member's new low edge, or null when the members plus their smallest gaps are longer
        /// than the span. A view carrying its own clearance keeps a wider gap than its neighbours,
        /// which is the point of the per-view margin, so the gaps come out equal only where the
        /// minimums are.
        /// </returns>
        public static double[] Spread(IReadOnlyList<double> sizes, IReadOnlyList<double> minGaps,
            double spanMin, double spanMax)
        {
            if (sizes == null) throw new ArgumentNullException(nameof(sizes));
            int n = sizes.Count;
            if (n == 0) return Array.Empty<double>();
            if (minGaps == null || minGaps.Count != Math.Max(0, n - 1))
                throw new ArgumentException("One gap is needed between each pair of members.", nameof(minGaps));

            double total = sizes.Sum();
            double minimum = minGaps.Sum();
            double span = spanMax - spanMin;
            if (total + minimum > span + Eps) return null;

            double extra = n > 1 ? (span - total - minimum) / (n - 1) : 0;

            var result = new double[n];
            double cursor = spanMin;
            for (int i = 0; i < n; i++)
            {
                result[i] = cursor;
                cursor += sizes[i];
                if (i < n - 1) cursor += minGaps[i] + extra;
            }
            return result;
        }
    }
}
