using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core
{
    /// <summary>Finds overlapping annotation boxes in one view. Pure maths, no Revit.</summary>
    public static class OverlapFinder
    {
        /// <summary>
        /// Smallest overlap depth (paper mm) that counts. Boxes that only touch, or overlap by rounding
        /// noise, are not reported.
        /// </summary>
        public const double MinOverlapMm = 0.01;

        /// <summary>
        /// Returns every overlapping pair once, the smaller element id as <see cref="OverlapPair.A"/>,
        /// sorted by overlap depth, largest first.
        /// </summary>
        /// <param name="boxes">Boxes of one view, in paper mm. Null entries are ignored.</param>
        /// <param name="acrossTypes">
        /// True compares every pair; false compares only boxes with the same <see cref="PaperBox.TypeName"/>.
        /// </param>
        public static List<OverlapPair> Find(IReadOnlyList<PaperBox> boxes, bool acrossTypes)
        {
            var result = new List<OverlapPair>();
            if (boxes == null || boxes.Count < 2)
                return result;

            IEnumerable<List<PaperBox>> groups = acrossTypes
                ? new[] { boxes.Where(b => b != null).ToList() }
                : boxes.Where(b => b != null).GroupBy(b => b.TypeName ?? "").Select(g => g.ToList());

            foreach (List<PaperBox> group in groups)
                Sweep(group, result);

            return result
                .OrderByDescending(p => p.OverlapMm)
                .ThenBy(p => p.A.Id)
                .ThenBy(p => p.B.Id)
                .ToList();
        }

        /// <summary>
        /// Converts a model distance in mm to millimetres on paper for a view scale of 1:<paramref name="viewScale"/>.
        /// A scale below 1 (sheets, unset) is treated as 1.
        /// </summary>
        public static double ModelMmToPaperMm(double modelMm, int viewScale) =>
            modelMm / (viewScale < 1 ? 1 : viewScale);

        /// <summary>Sort by left edge, then compare each box only with the boxes still open on its left.</summary>
        private static void Sweep(List<PaperBox> group, List<OverlapPair> result)
        {
            group.Sort((a, b) => a.X.CompareTo(b.X));
            var active = new List<PaperBox>();

            foreach (PaperBox box in group)
            {
                active.RemoveAll(a => a.Right - box.X < MinOverlapMm);

                foreach (PaperBox other in active)
                {
                    if (other.Id == box.Id)
                        continue;

                    double dx = Math.Min(other.Right, box.Right) - Math.Max(other.X, box.X);
                    double dy = Math.Min(other.Top, box.Top) - Math.Max(other.Y, box.Y);
                    double depth = Math.Min(dx, dy);
                    if (depth < MinOverlapMm)
                        continue;

                    bool boxFirst = box.Id < other.Id;
                    result.Add(new OverlapPair
                    {
                        A = boxFirst ? box : other,
                        B = boxFirst ? other : box,
                        OverlapMm = Math.Round(depth, 2)
                    });
                }

                active.Add(box);
            }
        }
    }
}
