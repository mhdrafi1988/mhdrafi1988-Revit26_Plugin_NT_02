using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers;
using Xunit;

namespace AnnotationOverlapDetection.Tests
{
    /// <summary>V006: By priority mode and the priority order presets.</summary>
    public class PriorityTests
    {
        // Upper box (id 1): y 8..18. Lower box (id 2): y 0..10. Overlap 2 mm, gap 1 mm => distance 3 mm.
        private static Dictionary<long, ResolveBox> TwoBoxes() => new()
        {
            [1] = new ResolveBox { Id = 1, X = 0, Y = 8, Width = 40, Height = 10 },
            [2] = new ResolveBox { Id = 2, X = 5, Y = 0, Width = 40, Height = 10 },
        };

        private static readonly (long, long)[] OnePair = { (1, 2) };

        private static PriorityOptions Ranks(int rank1, int rank2, TieRule tie = TieRule.Distribute,
            bool fixed1 = false, bool fixed2 = false) => new()
        {
            Rank = id => id == 1 ? rank1 : rank2,
            NeverMove = id => id == 1 ? fixed1 : fixed2,
            Tie = tie
        };

        [Fact]
        public void LowerPriorityUpper_MovesUp()
        {
            // 1 = text note (rank 5) above 2 = dimension (rank 0): the note moves up, the dimension stays.
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0, null, Ranks(5, 0));

            Assert.Single(plan.MovesMm);
            Assert.Equal(3.0, plan.MovesMm[1], 6);
            Assert.Equal(new long[] { 1 }, plan.Pairs[0].MovedIds);
        }

        [Fact]
        public void LowerPriorityLower_MovesDown()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0, null, Ranks(0, 5));

            Assert.Single(plan.MovesMm);
            Assert.Equal(-3.0, plan.MovesMm[2], 6);
        }

        [Theory]
        [InlineData(TieRule.Distribute, 1.5, -1.5)]
        [InlineData(TieRule.MoveUp, 3.0, 0.0)]
        [InlineData(TieRule.MoveDown, 0.0, -3.0)]
        public void SamePriority_FollowsTieRule(TieRule tie, double move1, double move2)
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0, null, Ranks(2, 2, tie));

            Assert.Equal(move1, plan.MovesMm.TryGetValue(1, out var m1) ? m1 : 0, 6);
            Assert.Equal(move2, plan.MovesMm.TryGetValue(2, out var m2) ? m2 : 0, 6);
        }

        [Fact]
        public void SamePriority_Skip_LeavesPair()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0, null, Ranks(2, 2, TieRule.Skip));

            Assert.Empty(plan.MovesMm);
            Assert.Equal(PairOutcome.Skipped, plan.Pairs[0].Outcome);
            Assert.Equal("same priority", plan.Pairs[0].Reason);
        }

        [Fact]
        public void NeverMove_WinsOverRank()
        {
            // Element 2 has the lower priority but is set to never move: element 1 moves instead.
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0, null, Ranks(0, 5, fixed2: true));

            Assert.Equal(3.0, plan.MovesMm[1], 6);
            Assert.False(plan.MovesMm.ContainsKey(2));
        }

        [Fact]
        public void BothNeverMove_Skipped()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0, null,
                Ranks(0, 5, fixed1: true, fixed2: true));

            Assert.Equal(PairOutcome.Skipped, plan.Pairs[0].Outcome);
            Assert.Empty(plan.MovesMm);
        }

        [Fact]
        public void PinnedMover_StillSkipped()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.ByPriority, 1.0,
                id => id == 1 ? "pinned" : null, Ranks(5, 0));

            Assert.Equal(PairOutcome.Skipped, plan.Pairs[0].Outcome);
            Assert.Equal("1 pinned", plan.Pairs[0].Reason);
        }

        [Fact]
        public void RecommendedOrder_DimensionsFirst_TextNotesLast()
        {
            var order = AnnotationPriority.OrderFor(PriorityPreset.Architectural);

            Assert.Equal(AnnotationCategory.Dimension, order.First());
            Assert.Equal(AnnotationCategory.TextNote, order.Last());
            Assert.Equal(AnnotationPriority.AllCategories.Count, order.Distinct().Count());
        }

        [Fact]
        public void EveryPreset_ListsEveryCategoryOnce()
        {
            foreach (var p in new[] { PriorityPreset.Architectural, PriorityPreset.NotesStay, PriorityPreset.TagsStay })
            {
                var order = AnnotationPriority.OrderFor(p);
                Assert.Equal(AnnotationPriority.AllCategories.OrderBy(c => c), order.OrderBy(c => c));
            }
        }

        [Fact]
        public void MatchPreset_RecognisesPresetsAndCustom()
        {
            Assert.Equal(PriorityPreset.NotesStay, AnnotationPriority.MatchPreset(AnnotationPriority.OrderFor(PriorityPreset.NotesStay)));

            var custom = AnnotationPriority.ArchitecturalOrder.Reverse().ToList();
            Assert.Equal(PriorityPreset.Custom, AnnotationPriority.MatchPreset(custom));
        }

        [Fact]
        public void Normalize_DropsUnknownAndDuplicates_AppendsMissing()
        {
            var order = AnnotationPriority.Normalize(new[] { "TextNote", "Bogus", "TextNote", "Dimension" });

            Assert.Equal(AnnotationCategory.TextNote, order[0]);
            Assert.Equal(AnnotationCategory.Dimension, order[1]);
            Assert.Equal(AnnotationPriority.AllCategories.Count, order.Count);
            Assert.Equal(order.Count, order.Distinct().Count());
        }

        [Fact]
        public void Normalize_NullGivesRecommendedOrder()
        {
            Assert.Equal(AnnotationPriority.ArchitecturalOrder, AnnotationPriority.Normalize(null));
        }
    }
}
