using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core
{
    /// <summary>
    /// Picks the level nearest an elevation. Pure arithmetic on <see cref="LevelCandidate"/>, with no Revit
    /// API, so the rules can be unit-tested.
    /// </summary>
    public static class NearestLevelCalculator
    {
        /// <summary>
        /// The candidate level nearest <paramref name="elevationMm"/> under <paramref name="rule"/>, and the
        /// signed distance from that level up to the elevation. Ties go to the lower level.
        /// Returns <see cref="NearestMatch.None"/> when no candidate matches the rule.
        /// </summary>
        public static NearestMatch Find(IReadOnlyList<LevelCandidate> candidates, double elevationMm,
            NearestRule rule, double toleranceMm = 1.0)
        {
            if (candidates == null || candidates.Count == 0) return NearestMatch.None;
            double tolerance = Math.Max(0, toleranceMm);

            LevelCandidate best = null;
            double bestDistance = double.MaxValue;

            foreach (LevelCandidate c in candidates)
            {
                if (c == null) continue;
                double signed = elevationMm - c.ElevationMm;   // + when the element is above the level
                if (!Allowed(signed, rule, tolerance)) continue;

                double distance = Math.Abs(signed);
                if (best == null || distance < bestDistance - 1e-9 ||
                    (Math.Abs(distance - bestDistance) <= 1e-9 && c.ElevationMm < best.ElevationMm))
                {
                    best = c;
                    bestDistance = distance;
                }
            }

            if (best == null) return NearestMatch.None;
            double offset = elevationMm - best.ElevationMm;
            if (Math.Abs(offset) <= tolerance) offset = 0;
            return new NearestMatch(true, best.Id, best.Name, offset);
        }

        // A level exactly at the elevation (inside the tolerance) satisfies every rule, so an element sitting
        // on a level never reports the level above or below it instead.
        private static bool Allowed(double signed, NearestRule rule, double tolerance) => rule switch
        {
            NearestRule.ClosestAtOrBelow => signed >= -tolerance,
            NearestRule.ClosestAtOrAbove => signed <= tolerance,
            _ => true
        };

        /// <summary>Label for the rule, as the window and the log show it.</summary>
        public static string RuleLabel(NearestRule rule) => rule switch
        {
            NearestRule.ClosestAtOrBelow => "Closest at or below",
            NearestRule.ClosestAtOrAbove => "Closest at or above",
            _ => "Closest in either direction"
        };
    }
}
