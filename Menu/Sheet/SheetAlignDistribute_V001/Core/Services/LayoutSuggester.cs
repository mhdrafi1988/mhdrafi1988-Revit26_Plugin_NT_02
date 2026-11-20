using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services
{
    /// <summary>One combination of the four align and distribute settings.</summary>
    public sealed record LayoutCombo(RowAlign Rows, ColumnAlign Columns,
        DistributeMode Horizontal, DistributeMode Vertical)
    {
        // Listed in the order a tie is settled in: the plain, title-friendly choices first, so when two
        // combinations give the same result the button keeps the name that is easiest to read.
        private static readonly RowAlign[] RowOrder = { RowAlign.Bottom, RowAlign.Top, RowAlign.Center, RowAlign.LeastMovement };
        private static readonly ColumnAlign[] ColumnOrder = { ColumnAlign.Left, ColumnAlign.Right, ColumnAlign.Center, ColumnAlign.LeastMovement };
        private static readonly DistributeMode[] DistributeOrder = { DistributeMode.Off, DistributeMode.EvenGaps, DistributeMode.FitMargins };

        /// <summary>
        /// Every combination worth trying, in tie-break order: 4 row × 4 column × 3 × 3 distribute = 144.
        /// "Off" alignment is left out, since a suggestion that aligns nothing is not a suggestion.
        /// </summary>
        public static IReadOnlyList<LayoutCombo> All { get; } =
            (from r in RowOrder
             from c in ColumnOrder
             from h in DistributeOrder
             from v in DistributeOrder
             select new LayoutCombo(r, c, h, v)).ToList();

        /// <summary>Short name for a button, e.g. "Bottom + Left · gaps kept".</summary>
        public string Title => $"{RowName} + {ColumnName} · {DistributeName}";

        /// <summary>One plain sentence on what this combination does to the sheet.</summary>
        public string Description => Rows switch
        {
            RowAlign.Bottom => "Titles line up.",
            RowAlign.Top => "Tops line up; titles follow each view's height.",
            RowAlign.Center => "Rows share a centre line.",
            _ => "Each row takes whichever edge moves it least."
        };

        private string RowName => Rows switch
        {
            RowAlign.Bottom => "Bottom",
            RowAlign.Top => "Top",
            RowAlign.Center => "Centre",
            RowAlign.LeastMovement => "Least move",
            _ => "Off"
        };

        private string ColumnName => Columns switch
        {
            ColumnAlign.Left => "Left",
            ColumnAlign.Right => "Right",
            ColumnAlign.Center => "Centre",
            ColumnAlign.LeastMovement => "Least move",
            _ => "Off"
        };

        private string DistributeName
        {
            get
            {
                var parts = new List<string>();
                if (Horizontal == DistributeMode.EvenGaps) parts.Add("even gaps across rows");
                if (Horizontal == DistributeMode.FitMargins) parts.Add("rows fit the width");
                if (Vertical == DistributeMode.EvenGaps) parts.Add("even gaps between rows");
                if (Vertical == DistributeMode.FitMargins) parts.Add("rows fit the height");
                return parts.Count == 0 ? "gaps kept" : string.Join(" + ", parts);
            }
        }
    }

    /// <summary>What one test plan of a <see cref="LayoutCombo"/> came out as. Nothing is moved to get this.</summary>
    public sealed class CandidateOutcome
    {
        /// <summary>The combination that was planned.</summary>
        public LayoutCombo Combo { get; init; }

        /// <summary>The plan's signature; two candidates with the same one end with the views in the same places.</summary>
        public string Signature { get; init; }

        /// <summary>True when the plan cannot be applied at all (two views would overlap, or no usable area).</summary>
        public bool IsBlocked { get; init; }

        /// <summary>Views the plan moves.</summary>
        public int Moves { get; init; }

        /// <summary>Views already on their line.</summary>
        public int InPlace { get; init; }

        /// <summary>Views that need more than the move limit and are left where they are.</summary>
        public int OverLimit { get; init; }

        /// <summary>Views whose aligned position would cross the margins and are left where they are.</summary>
        public int CrossMargins { get; init; }

        /// <summary>Sum of every move the plan makes (mm).</summary>
        public double TotalTravelMm { get; init; }

        /// <summary>The longest move the plan makes (mm).</summary>
        public double LargestMoveMm { get; init; }

        /// <summary>Where every element on the sheet ends up, for the button's small sketch.</summary>
        public IReadOnlyList<SketchItem> Sketch { get; init; } = Array.Empty<SketchItem>();

        /// <summary>The outline the sketch is drawn inside (the title block), or null.</summary>
        public LayoutRect? Frame { get; init; }

        /// <summary>Views that overflow: over the move limit or across the margins.</summary>
        public int Overflow => OverLimit + CrossMargins;

        /// <summary>Views that end up on a shared line, moved or already there.</summary>
        public int Aligned => Moves + InPlace;
    }

    /// <summary>One element in a suggestion's sketch: where it ends up, and what happens to it.</summary>
    public readonly record struct SketchItem(LayoutRect Target, AlignStatus Status);

    /// <summary>One suggestion button.</summary>
    public sealed class LayoutSuggestion
    {
        /// <summary>1-based place in the ranking; 1 is the best fit.</summary>
        public int Rank { get; init; }

        /// <summary>The test plan this suggestion stands for.</summary>
        public CandidateOutcome Outcome { get; init; }

        /// <summary>Lower-ranked combinations that give exactly the same result, so were merged into this one.</summary>
        public IReadOnlyList<LayoutCombo> SameResult { get; init; } = Array.Empty<LayoutCombo>();

        /// <summary>The combination the button loads.</summary>
        public LayoutCombo Combo => Outcome.Combo;
    }

    /// <summary>The suggestions for one sheet, plus how they were picked.</summary>
    public sealed class SuggestionSet
    {
        /// <summary>The best few distinct results, best first.</summary>
        public List<LayoutSuggestion> Suggestions { get; init; } = new();

        /// <summary>Combinations tried.</summary>
        public int Tried { get; init; }

        /// <summary>Combinations that could not be applied (overlap or no usable area).</summary>
        public int Blocked { get; init; }

        /// <summary>Combinations that moved nothing.</summary>
        public int NothingToMove { get; init; }

        /// <summary>Different results among the rest.</summary>
        public int Distinct { get; init; }

        /// <summary>One line for under the buttons.</summary>
        public string Summary => Tried == 0
            ? "Nothing to suggest on this sheet."
            : Suggestions.Count == 0
                ? $"Tried {Tried} combinations: none would move anything without an overlap."
                : $"Picked from {Tried} test plans on this sheet. {Blocked} were blocked by an overlap, " +
                  $"{NothingToMove} moved nothing, and the rest gave {Distinct} different result(s). " +
                  "Ranked by: fewest overflowing views, then most views aligned, then least total travel.";
    }

    /// <summary>
    /// Picks the few best, distinct layouts from a batch of test plans. Revit-free, so the ranking is
    /// unit tested (<c>Tests/SheetAlignDistribute.Tests</c>); the plans themselves come from
    /// <see cref="AlignPlanner.Suggest"/>.
    /// </summary>
    public static class LayoutSuggester
    {
        /// <summary>How many buttons the window shows.</summary>
        public const int DefaultCount = 4;

        /// <summary>
        /// Drops candidates that are blocked or move nothing, ranks the rest, merges those that end
        /// with the views in the same places, and returns the top <paramref name="count"/>.
        /// Ranking: fewest overflowing views (over the limit or across the margins), then most views
        /// aligned, then least total travel, then the smallest largest move. Ties keep the input order.
        /// Overflowing candidates stay in: the user decides on Apply whether to place those views.
        /// </summary>
        public static SuggestionSet Pick(IReadOnlyList<CandidateOutcome> candidates, int count = DefaultCount)
        {
            if (candidates == null || candidates.Count == 0) return new SuggestionSet();

            int blocked = candidates.Count(c => c.IsBlocked);
            int nothing = candidates.Count(c => !c.IsBlocked && c.Moves == 0);

            var ranked = candidates
                .Select((c, index) => (c, index))
                .Where(x => !x.c.IsBlocked && x.c.Moves > 0)
                .OrderBy(x => x.c.Overflow)
                .ThenByDescending(x => x.c.Aligned)
                .ThenBy(x => Math.Round(x.c.TotalTravelMm, 1))
                .ThenBy(x => Math.Round(x.c.LargestMoveMm, 1))
                .ThenBy(x => x.index)
                .Select(x => x.c)
                .ToList();

            // Group by outcome, keeping each group's best-ranked member as its face.
            var groups = new List<(CandidateOutcome Face, List<LayoutCombo> Same)>();
            var bySignature = new Dictionary<string, int>();
            foreach (var candidate in ranked)
            {
                string key = candidate.Signature ?? candidate.Combo.ToString();
                if (bySignature.TryGetValue(key, out int at))
                {
                    groups[at].Same.Add(candidate.Combo);
                }
                else
                {
                    bySignature[key] = groups.Count;
                    groups.Add((candidate, new List<LayoutCombo>()));
                }
            }

            return new SuggestionSet
            {
                Tried = candidates.Count,
                Blocked = blocked,
                NothingToMove = nothing,
                Distinct = groups.Count,
                Suggestions = groups
                    .Take(Math.Max(0, count))
                    .Select((g, i) => new LayoutSuggestion { Rank = i + 1, Outcome = g.Face, SameResult = g.Same })
                    .ToList()
            };
        }
    }
}
