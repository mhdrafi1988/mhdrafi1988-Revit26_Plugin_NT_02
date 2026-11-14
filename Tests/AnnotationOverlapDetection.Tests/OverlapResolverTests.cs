using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Helpers;
using Xunit;

namespace AnnotationOverlapDetection.Tests
{
    public class OverlapResolverTests
    {
        // Upper box (id 1): y 8..18. Lower box (id 2): y 0..10. Vertical overlap = 2 mm.
        private static Dictionary<long, ResolveBox> TwoBoxes() => new()
        {
            [1] = new ResolveBox { Id = 1, X = 0, Y = 8, Width = 40, Height = 10 },
            [2] = new ResolveBox { Id = 2, X = 5, Y = 0, Width = 40, Height = 10 },
        };

        private static readonly (long, long)[] OnePair = { (1, 2) };

        [Fact]
        public void MoveUp_MovesUpperByOverlapPlusGap()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.MoveUp, 1.0);

            Assert.Single(plan.MovesMm);
            Assert.Equal(3.0, plan.MovesMm[1], 6);
            Assert.Equal(PairOutcome.Planned, plan.Pairs[0].Outcome);
        }

        [Fact]
        public void MoveDown_MovesLowerDown()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.MoveDown, 1.0);

            Assert.Single(plan.MovesMm);
            Assert.Equal(-3.0, plan.MovesMm[2], 6);
        }

        [Fact]
        public void Distribute_SplitsDistanceBetweenBoth()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.Distribute, 1.0);

            Assert.Equal(1.5, plan.MovesMm[1], 6);
            Assert.Equal(-1.5, plan.MovesMm[2], 6);
        }

        [Fact]
        public void UpperIsDecidedByPosition_NotByGridOrder()
        {
            // Pair listed as (lower, upper): Move Up still moves element 1, the upper one.
            var plan = OverlapResolver.Plan(TwoBoxes(), new[] { (2L, 1L) }, ResolveMode.MoveUp, 0);

            Assert.True(plan.MovesMm.ContainsKey(1));
            Assert.False(plan.MovesMm.ContainsKey(2));
            Assert.Equal(2.0, plan.MovesMm[1], 6);
        }

        [Fact]
        public void BlockedElement_SkipsPairAndMovesNothing()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.Distribute, 1.0,
                id => id == 2 ? "pinned" : null);

            Assert.Empty(plan.MovesMm);
            Assert.Equal(PairOutcome.Skipped, plan.Pairs[0].Outcome);
            Assert.Equal("2 pinned", plan.Pairs[0].Reason);
        }

        [Fact]
        public void BlockedElementThatDoesNotMove_DoesNotBlock()
        {
            // Move Up only moves the upper element, so a pinned lower element is fine.
            var plan = OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.MoveUp, 1.0,
                id => id == 2 ? "pinned" : null);

            Assert.Equal(PairOutcome.Planned, plan.Pairs[0].Outcome);
            Assert.Equal(3.0, plan.MovesMm[1], 6);
        }

        [Fact]
        public void SecondPairClearedByFirstMove_IsAlreadyClear()
        {
            // The same pair listed twice: the second entry sees the planned move and needs nothing more.
            var boxes = TwoBoxes();
            var plan = OverlapResolver.Plan(boxes, new[] { (1L, 2L), (1L, 2L) }, ResolveMode.MoveUp, 0.5);

            Assert.Equal(PairOutcome.Planned, plan.Pairs[0].Outcome);
            Assert.Equal(PairOutcome.AlreadyClear, plan.Pairs[1].Outcome);
            Assert.Equal(2.5, plan.MovesMm[1], 6);
        }

        [Fact]
        public void ChainedPairs_AccumulateMovesOnSharedElement()
        {
            // 3 stacked boxes, each overlapping the next by 2 mm. Move Up: 2 goes up over 1, then 3 over 2's new spot.
            var boxes = new Dictionary<long, ResolveBox>
            {
                [1] = new ResolveBox { Id = 1, X = 0, Y = 0, Width = 10, Height = 10 },
                [2] = new ResolveBox { Id = 2, X = 0, Y = 8, Width = 10, Height = 10 },
                [3] = new ResolveBox { Id = 3, X = 0, Y = 16, Width = 10, Height = 10 },
            };
            var plan = OverlapResolver.Plan(boxes, new[] { (1L, 2L), (2L, 3L) }, ResolveMode.MoveUp, 0);

            Assert.Equal(2.0, plan.MovesMm[2], 6);   // 2: 8 -> 10
            Assert.Equal(4.0, plan.MovesMm[3], 6);   // 3: 16 -> 20 (clear of 2's new top at 20)
        }

        [Fact]
        public void InputBoxesAreNotChanged()
        {
            var boxes = TwoBoxes();
            OverlapResolver.Plan(boxes, OnePair, ResolveMode.Distribute, 1.0);

            Assert.Equal(8.0, boxes[1].Y);
            Assert.Equal(0.0, boxes[2].Y);
        }

        [Fact]
        public void MissingElement_IsSkipped()
        {
            var plan = OverlapResolver.Plan(TwoBoxes(), new[] { (1L, 99L) }, ResolveMode.MoveUp, 1.0);

            Assert.Equal(PairOutcome.Skipped, plan.Pairs[0].Outcome);
            Assert.Empty(plan.MovesMm);
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1000.1)]
        [InlineData(double.NaN)]
        public void InvalidGap_Throws(double gap)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OverlapResolver.Plan(TwoBoxes(), OnePair, ResolveMode.MoveUp, gap));
        }

        [Fact]
        public void TouchingEdges_DoNotIntersect()
        {
            var a = new ResolveBox { X = 0, Y = 0, Width = 10, Height = 10 };
            var b = new ResolveBox { X = 0, Y = 10, Width = 10, Height = 10 };
            Assert.False(OverlapResolver.Intersect(a, b));
        }
    }
}
