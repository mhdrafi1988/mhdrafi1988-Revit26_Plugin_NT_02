using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Joint positions along one row, measured in the row direction from hip (a0) to hip (b0) on the
    /// row's eave-side line. Both ends are always included. One method per row-start option.
    /// </summary>
    public static class RowJoints
    {
        private const double Eps = 1e-9;

        public static List<double> For(RowStartMode mode, MiddleStartVariant variant, double a0, double b0, double length, double minPiece) =>
            For(mode, variant, a0, b0, length, minPiece, a0, b0, 0);

        /// <param name="ai">Hip point on the row's upper line (left).</param>
        /// <param name="bi">Hip point on the row's upper line (right).</param>
        /// <param name="hipStrip">Row start E: extra hip-piece width along the row.</param>
        public static List<double> For(RowStartMode mode, MiddleStartVariant variant, double a0, double b0, double length,
            double minPiece, double ai, double bi, double hipStrip) =>
            b0 - a0 <= length + Eps
                ? new List<double> { a0, b0 } // a row no longer than one board stays one piece
                : mode switch
            {
                RowStartMode.FromUpperHip => FromUpperHip(a0, b0, ai, bi, length, minPiece, hipStrip),
                RowStartMode.HipsSplitLeftover => SplitAtHips(a0, b0, length, minPiece),
                RowStartMode.OneHip => FromOneHip(a0, b0, length, minPiece),
                RowStartMode.FromMiddle => variant == MiddleStartVariant.BoardCentred
                    ? FromMiddle(a0, b0, length, boardCentred: true)
                    : FromMiddle(a0, b0, length, boardCentred: false),
                _ => MiddleFiller(a0, b0, length, minPiece)
            };

        /// <summary>
        /// A — full boards from both hips inward, one filler in the middle (V003). V004 change: a filler
        /// under the minimum piece size becomes a centred full board with two equal cuts beside it.
        /// </summary>
        public static List<double> MiddleFiller(double a0, double b0, double length, double minPiece)
        {
            double mid = (a0 + b0) / 2;
            var all = new List<double> { a0 };
            for (int k = 1; a0 + k * length <= mid + Eps; k++)
            {
                all.Add(a0 + k * length);
            }

            var right = new List<double> { b0 };
            for (int k = 1; b0 - k * length >= mid - Eps; k++)
            {
                right.Add(b0 - k * length);
            }

            double gap = right[right.Count - 1] - all[all.Count - 1];
            if (gap > Eps && gap < minPiece - Eps && all.Count >= 2 && right.Count >= 2)
            {
                // Filler would be a sliver: take out the board on each side of it, centre one full
                // board and leave two equal cuts of (gap + L) / 2.
                all.RemoveAt(all.Count - 1);
                right.RemoveAt(right.Count - 1);
                all.Add(mid - length / 2);
                all.Add(mid + length / 2);
            }
            else if (gap > length + Eps)
            {
                double slip = (gap - length) / 2;
                if (slip >= minPiece - Eps)
                {
                    all.Add(mid - length / 2);
                    all.Add(mid + length / 2);
                }
                else
                {
                    all.Add(mid);
                }
            }

            all.AddRange(right);
            return Clean(all);
        }

        /// <summary>
        /// E — full boards start where the hip crosses the row's upper line (+ hip strip width), so each
        /// hip piece is a clean triangle (or a fixed-width strip piece); one filler in the middle.
        /// The hip piece is capped at one board length along the row.
        /// </summary>
        public static List<double> FromUpperHip(double a0, double b0, double ai, double bi, double length, double minPiece, double hipStrip)
        {
            double s0 = Math.Min(Math.Max(ai, a0) + hipStrip, a0 + length);
            double s1 = Math.Max(Math.Min(bi, b0) - hipStrip, b0 - length);
            if (s1 - s0 < minPiece - Eps)
            {
                return MiddleFiller(a0, b0, length, minPiece); // top rows near the apex: fall back to A
            }

            List<double> inner = MiddleFiller(s0, s1, length, minPiece);
            inner.Add(a0);
            inner.Add(b0);
            return Clean(inner);
        }

        /// <summary>Regular joints a0 + offset + k·L strictly inside (a0, b0): the base grid for best fit.</summary>
        public static List<double> Grid(double a0, double b0, double offset, double length)
        {
            var inner = new List<double>();
            for (double j = a0 + offset; j < b0 - Eps; j += length)
            {
                if (j > a0 + Eps)
                {
                    inner.Add(j);
                }
            }

            return inner;
        }

        /// <summary>
        /// B — no middle cut: n full boards centred in the row, leftover r split into two equal end
        /// pieces. If r/2 is under the minimum, one board comes out so the end pieces become (r + L)/2.
        /// </summary>
        public static List<double> SplitAtHips(double a0, double b0, double length, double minPiece)
        {
            double e = b0 - a0;
            int n = (int)Math.Floor(e / length + Eps);
            double r = e - n * length;
            if (r / 2 > Eps && r / 2 < minPiece - Eps && n >= 1)
            {
                n--;
                r += length;
            }

            var all = new List<double> { a0, b0 };
            for (int k = 0; k <= n; k++)
            {
                all.Add(a0 + r / 2 + k * length);
            }

            return Clean(all);
        }

        /// <summary>
        /// C — no middle cut: full boards from the left hip; the leftover r sits at the right-hand hip.
        /// If r is under the minimum, the last two pieces become two equal pieces of (L + r)/2.
        /// </summary>
        public static List<double> FromOneHip(double a0, double b0, double length, double minPiece)
        {
            var all = new List<double>();
            for (int k = 0; a0 + k * length < b0 - Eps; k++)
            {
                all.Add(a0 + k * length);
            }

            all.Add(b0);
            double r = all[all.Count - 1] - all[all.Count - 2];
            if (r > Eps && r < minPiece - Eps && all.Count >= 3)
            {
                all[all.Count - 2] = b0 - (length + r) / 2;
            }

            return Clean(all);
        }

        /// <summary>D — start at the exact middle: D1 a joint on the centre line, D2 a board centred on it.</summary>
        public static List<double> FromMiddle(double a0, double b0, double length, bool boardCentred)
        {
            double mid = (a0 + b0) / 2;
            double start = boardCentred ? mid - length / 2 : mid;
            var all = new List<double> { a0, b0 };
            int kMin = (int)Math.Floor((a0 - start) / length) - 1;
            int kMax = (int)Math.Ceiling((b0 - start) / length) + 1;
            for (int k = kMin; k <= kMax; k++)
            {
                double j = start + k * length;
                if (j > a0 + Eps && j < b0 - Eps)
                {
                    all.Add(j);
                }
            }

            return Clean(all);
        }

        /// <summary>Shift of row <paramref name="rowsAboveFirst"/> (0 = first full row) for a joint pattern.</summary>
        public static double StaggerShift(JointPattern pattern, int rowsAboveFirst, double length) => pattern switch
        {
            JointPattern.StaggerHalf => (rowsAboveFirst % 2) * length / 2,
            JointPattern.StaggerThird => (rowsAboveFirst % 3) * length / 3,
            _ => 0
        };

        /// <summary>
        /// Carries the first row's inner joints into another row: shift them, extend them by whole boards
        /// at both ends so no gap is longer than one board, and keep the ones inside this row's hips.
        /// </summary>
        public static List<double> Continue(IReadOnlyList<double> baseInner, double shift, double a0, double b0, double length)
        {
            if (b0 - a0 <= length + Eps || baseInner.Count == 0)
            {
                return new List<double> { a0, b0 };
            }

            var shifted = new List<double>();
            foreach (double j in baseInner)
            {
                shifted.Add(j + shift);
            }

            shifted.Sort();
            for (double j = shifted[0] - length; j > a0 + Eps; j -= length)
            {
                shifted.Add(j);
            }

            for (double j = shifted.Max() + length; j < b0 - Eps; j += length)
            {
                shifted.Add(j);
            }

            var all = new List<double> { a0, b0 };
            foreach (double j in shifted)
            {
                if (j > a0 + Eps && j < b0 - Eps)
                {
                    all.Add(j);
                }
            }

            return Clean(all);
        }

        private static List<double> Clean(List<double> joints)
        {
            joints.Sort();
            var unique = new List<double>();
            foreach (double j in joints)
            {
                if (unique.Count == 0 || j - unique[unique.Count - 1] > GeometryTolerance.Vertex)
                {
                    unique.Add(j);
                }
            }

            return unique;
        }
    }
}
