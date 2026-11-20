using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SheetAlignDistribute.Tests
{
    /// <summary>The "Suggested layouts" ranking: what is dropped, how the rest are ordered and merged.</summary>
    public class LayoutSuggesterTests
    {
        private static LayoutCombo Combo(int index) => LayoutCombo.All[index];

        private static CandidateOutcome Outcome(int combo, string signature, int moves = 3, int inPlace = 1,
            int overLimit = 0, int crossMargins = 0, double travel = 10, double largest = 5, bool blocked = false)
            => new()
            {
                Combo = Combo(combo),
                Signature = signature,
                Moves = moves,
                InPlace = inPlace,
                OverLimit = overLimit,
                CrossMargins = crossMargins,
                TotalTravelMm = travel,
                LargestMoveMm = largest,
                IsBlocked = blocked
            };

        [Fact]
        public void All_has_144_distinct_combinations_with_no_off_alignment()
        {
            Assert.Equal(144, LayoutCombo.All.Count);
            Assert.Equal(144, LayoutCombo.All.Distinct().Count());
            Assert.DoesNotContain(LayoutCombo.All, c => c.Rows == RowAlign.Off || c.Columns == ColumnAlign.Off);
        }

        [Fact]
        public void All_starts_with_bottom_left_gaps_kept_so_ties_keep_the_plain_name()
        {
            var first = LayoutCombo.All[0];
            Assert.Equal(new LayoutCombo(RowAlign.Bottom, ColumnAlign.Left, DistributeMode.Off, DistributeMode.Off), first);
            Assert.Equal("Bottom + Left · gaps kept", first.Title);
        }

        [Fact]
        public void Blocked_and_nothing_to_move_candidates_are_dropped_and_counted()
        {
            var set = LayoutSuggester.Pick(new List<CandidateOutcome>
            {
                Outcome(0, "a", blocked: true),
                Outcome(1, "b", moves: 0),
                Outcome(2, "c")
            });

            Assert.Equal(3, set.Tried);
            Assert.Equal(1, set.Blocked);
            Assert.Equal(1, set.NothingToMove);
            Assert.Single(set.Suggestions);
            Assert.Equal(Combo(2), set.Suggestions[0].Combo);
        }

        [Fact]
        public void Overflowing_candidates_are_kept_but_rank_below_clean_ones()
        {
            var set = LayoutSuggester.Pick(new List<CandidateOutcome>
            {
                Outcome(0, "over", moves: 6, inPlace: 2, overLimit: 1, travel: 1),
                Outcome(1, "margins", moves: 6, inPlace: 2, crossMargins: 2, travel: 1),
                Outcome(2, "clean", moves: 2, inPlace: 0, travel: 50)
            });

            Assert.Equal(new[] { "clean", "over", "margins" }, set.Suggestions.Select(s => s.Outcome.Signature));
        }

        [Fact]
        public void Ties_on_overflow_go_to_most_aligned_then_least_travel_then_smallest_largest_move()
        {
            var set = LayoutSuggester.Pick(new List<CandidateOutcome>
            {
                Outcome(0, "fewer aligned", moves: 2, inPlace: 1, travel: 1),
                Outcome(1, "more travel", moves: 3, inPlace: 1, travel: 20, largest: 4),
                Outcome(2, "bigger jump", moves: 3, inPlace: 1, travel: 10, largest: 9),
                Outcome(3, "best", moves: 3, inPlace: 1, travel: 10, largest: 4)
            });

            Assert.Equal(new[] { "best", "bigger jump", "more travel", "fewer aligned" },
                set.Suggestions.Select(s => s.Outcome.Signature));
        }

        [Fact]
        public void Equal_scores_keep_input_order()
        {
            var set = LayoutSuggester.Pick(new List<CandidateOutcome>
            {
                Outcome(5, "x"), Outcome(3, "y")
            });

            Assert.Equal(new[] { Combo(5), Combo(3) }, set.Suggestions.Select(s => s.Combo));
        }

        [Fact]
        public void Same_signature_is_merged_into_the_best_ranked_one()
        {
            var set = LayoutSuggester.Pick(new List<CandidateOutcome>
            {
                Outcome(0, "same"),
                Outcome(1, "other", travel: 30),
                Outcome(7, "same")
            });

            Assert.Equal(2, set.Distinct);
            Assert.Equal(2, set.Suggestions.Count);
            Assert.Equal(Combo(0), set.Suggestions[0].Combo);
            Assert.Equal(new[] { Combo(7) }, set.Suggestions[0].SameResult);
            Assert.Empty(set.Suggestions[1].SameResult);
        }

        [Fact]
        public void Only_the_requested_number_are_returned_and_ranked_from_one()
        {
            var candidates = Enumerable.Range(0, 10)
                .Select(i => Outcome(i, "s" + i, travel: i))
                .ToList();

            var set = LayoutSuggester.Pick(candidates);

            Assert.Equal(LayoutSuggester.DefaultCount, set.Suggestions.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, set.Suggestions.Select(s => s.Rank));
            Assert.Equal(10, set.Distinct);
        }

        [Fact]
        public void Nothing_in_gives_an_empty_set()
        {
            var set = LayoutSuggester.Pick(new List<CandidateOutcome>());
            Assert.Empty(set.Suggestions);
            Assert.Equal("Nothing to suggest on this sheet.", set.Summary);
        }

        [Fact]
        public void Titles_name_the_distribute_settings()
        {
            Assert.Equal("Centre + Right · even gaps across rows + rows fit the height",
                new LayoutCombo(RowAlign.Center, ColumnAlign.Right, DistributeMode.EvenGaps, DistributeMode.FitMargins).Title);
            Assert.Equal("Least move + Least move · gaps kept",
                new LayoutCombo(RowAlign.LeastMovement, ColumnAlign.LeastMovement, DistributeMode.Off, DistributeMode.Off).Title);
        }
    }
}
