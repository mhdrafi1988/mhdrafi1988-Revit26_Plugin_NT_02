// IslandRoofSplitter V002 — one place that decides which level the new roofs should default to, so the
// launch prompt (LevelTypePromptWindow) and the window (ViewModel.ApplyContext) always agree.
// Revit-free: it only sees the LevelOption list captured in SplitterContext.
//
// Added 2026-10-15 (per Anas): the source roof's own level wins; when it is missing, pre-select the level
// nearest the roof's top Z and report that as a guess so the UI can flag it. Never invents a level.
#nullable enable
using System;
using System.Collections.Generic;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Services
{
    using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

    public static class LevelResolver
    {
        /// <summary>
        /// Picks the level to start with. <paramref name="wasGuess"/> is true when the source roof's level
        /// could not be used and the nearest-elevation level was substituted.
        /// </summary>
        public static LevelOption? Resolve(IReadOnlyList<LevelOption> levels, long detectedId, bool detected,
                                           double? roofTopZFt, out bool wasGuess)
        {
            wasGuess = false;
            if (levels == null || levels.Count == 0) return null;

            if (detected)
                foreach (var l in levels)
                    if (l.Id == detectedId) return l;

            if (roofTopZFt is not double z) return null;

            LevelOption? best = null;
            double bestGap = double.MaxValue;
            foreach (var l in levels)
            {
                double gap = Math.Abs(l.ElevationFt - z);
                if (gap >= bestGap) continue;
                bestGap = gap;
                best = l;
            }
            wasGuess = best != null;
            return best;
        }

        /// <summary>Matches a roof type by id, falling back to null (never to an arbitrary type).</summary>
        public static NamedId? ResolveType(IReadOnlyList<NamedId> types, long detectedId, bool detected)
        {
            if (types == null || types.Count == 0 || !detected) return null;
            foreach (var t in types)
                if (t.Id == detectedId) return t;
            return null;
        }
    }
}
