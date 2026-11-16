using System.Collections.Generic;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V007.Helpers;
using Xunit;

namespace AnnotationOverlapDetection.Tests
{
    /// <summary>V007: keep clear of other annotations, neighbour tolerance, repeat passes.</summary>
    public class ClearanceTests
    {
        private static ResolveBox Box(long id, double x, double y, double w = 40, double h = 10) =>
            new() { Id = id, X = x, Y = y, Width = w, Height = h };

        private static ClearanceOptions Avoid(double tol = 0) => new() { AvoidOthers = true, NeighbourToleranceMm = tol };

        // Pair 1 (y 8..18) over 2 (y 0..10); neighbour 3 sits right where 1 would land (y 19..29).
        private static Dictionary<long, ResolveBox> PairWithNeighbourAbove() => new()
        {
            [1] = Box(1, 0, 8),
            [2] = Box(2, 5, 0),
            [3] = Box(3, 0, 19),
        };

        private static readonly (long, long)[] OnePair = { (1, 2) };

        [Fact]
        public void WithoutClearance_NeighbourIsIgnored_AsV006()
        {
            var plan = OverlapResolver.Plan(PairWithNeighbourAbove(), OnePair, ResolveMode.MoveUp, 1.0);

            Assert.Equal(3.0, plan.MovesMm[1], 6);            // lands on 3 (y 11..21 vs 19..29)
            Assert.Empty(plan.Pairs[0].PushedPast);
        }

        [Fact]
        public void MoveUp_IsPushedPastNeighbour()
        {
            var plan = OverlapResolver.Plan(PairWithNeighbourAbove(), OnePair, ResolveMode.MoveUp, 1.0, null, null, Avoid());

            Assert.Equal(PairOutcome.Planned, plan.Pairs[0].Outcome);
            Assert.Equal(21.0, plan.MovesMm[1], 6);           // 8 -> 29, on top of 3
            Assert.Equal(new long[] { 3 }, plan.Pairs[0].PushedPast);
            Assert.False(plan.MovesMm.ContainsKey(3));         // the neighbour never moves
        }

        [Fact]
        public void Tolerance_KeepsSpaceFromNeighbour()
        {
            var plan = OverlapResolver.Plan(PairWithNeighbourAbove(), OnePair, ResolveMode.MoveUp, 1.0, null, null, Avoid(2.0));

            Assert.Equal(23.0, plan.MovesMm[1], 6);           // 8 -> 31 = 3's top 29 + 2
        }

        [Fact]
        public void Tolerance_CountsANearMissAsInTheWay()
        {
            // 3 is 1 mm above where 1 would land: clear without tolerance, in the way with 2 mm.
            var boxes = new Dictionary<long, ResolveBox> { [1] = Box(1, 0, 8), [2] = Box(2, 5, 0), [3] = Box(3, 0, 22) };

            var noTol = OverlapResolver.Plan(boxes, OnePair, ResolveMode.MoveUp, 1.0, null, null, Avoid(0));
            var withTol = OverlapResolver.Plan(boxes, OnePair, ResolveMode.MoveUp, 1.0, null, null, Avoid(2.0));

            Assert.Equal(3.0, noTol.MovesMm[1], 6);           // 11..21, 1 mm below 3
            Assert.Equal(26.0, withTol.MovesMm[1], 6);        // 8 -> 34 = 3's top 32 + 2
        }

        [Fact]
        public void NeighbourFarToTheSide_IsNotInTheWay()
        {
            var boxes = new Dictionary<long, ResolveBox> { [1] = Box(1, 0, 8), [2] = Box(2, 5, 0), [3] = Box(3, 100, 19) };

            var plan = OverlapResolver.Plan(boxes, OnePair, ResolveMode.MoveUp, 1.0, null, null, Avoid(2.0));

            Assert.Equal(3.0, plan.MovesMm[1], 6);
        }

        [Fact]
        public void MoveDown_IsPushedBelowNeighbour()
        {
            // 2 (y 0..10) under 1 (y 8..18); neighbour 4 at y -12..-2 is where 2 would land.
            var boxes = new Dictionary<long, ResolveBox> { [1] = Box(1, 0, 8), [2] = Box(2, 5, 0), [4] = Box(4, 5, -12) };

            var plan = OverlapResolver.Plan(boxes, OnePair, ResolveMode.MoveDown, 1.0, null, null, Avoid(1.0));

            Assert.Equal(-23.0, plan.MovesMm[2], 6);          // top at -13 = 4's bottom -12 - 1
        }

        [Fact]
        public void Distribute_EachHalfAvoidsNeighbours()
        {
            var plan = OverlapResolver.Plan(PairWithNeighbourAbove(), OnePair, ResolveMode.Distribute, 1.0, null, null, Avoid());

            Assert.Equal(21.0, plan.MovesMm[1], 6);           // upper pushed past 3
            Assert.Equal(-1.5, plan.MovesMm[2], 6);           // lower: plain half
        }

        [Fact]
        public void StackOfNeighbours_IsClearedInOneMove()
        {
            var boxes = PairWithNeighbourAbove();
            boxes[5] = Box(5, 10, 28);                         // overlaps 3, further up

            var plan = OverlapResolver.Plan(boxes, OnePair, ResolveMode.MoveUp, 0, null, null, Avoid());

            Assert.Equal(30.0, plan.MovesMm[1], 6);           // 8 -> 38, top of 5
            Assert.Contains(3L, plan.Pairs[0].PushedPast);
            Assert.Contains(5L, plan.Pairs[0].PushedPast);
        }

        /// <summary>Asserts no two boxes overlap once the planned moves are applied.</summary>
        private static void AssertNoOverlaps(Dictionary<long, ResolveBox> boxes, ResolvePlan plan)
        {
            var after = new List<ResolveBox>();
            foreach (var b in boxes.Values)
                after.Add(Box(b.Id, b.X, b.Y + (plan.MovesMm.TryGetValue(b.Id, out var d) ? d : 0), b.Width, b.Height));
            for (int i = 0; i < after.Count; i++)
                for (int j = i + 1; j < after.Count; j++)
                    Assert.False(OverlapResolver.Intersect(after[i], after[j]), $"{after[i].Id} overlaps {after[j].Id}");
        }

        [Fact]
        public void LaterMove_DoesNotLandOnEarlierMove()
        {
            // Pair (1,2) on the left, pair (3,4) partly above it. Distribute moves 1 up to 9..19 and,
            // without clearance, 4 down to 18.75..28.75: on top of 1's new spot.
            var boxes = new Dictionary<long, ResolveBox>
            {
                [1] = Box(1, 0, 8), [2] = Box(2, 0, 0),
                [3] = Box(3, 30, 28), [4] = Box(4, 30, 19.5),
            };
            var pairs = new[] { (1L, 2L), (3L, 4L) };

            var old = OverlapResolver.Plan(boxes, pairs, ResolveMode.Distribute, 0);
            var plan = OverlapResolver.Plan(boxes, pairs, ResolveMode.Distribute, 0, null, null, Avoid());

            Assert.Equal(-0.75, old.MovesMm[4], 6);           // V006 behaviour: lands on 1
            Assert.Contains(1L, plan.Pairs[1].PushedPast);
            AssertNoOverlaps(boxes, plan);
        }

        [Fact]
        public void ChainedPairs_EndWithNoOverlaps()
        {
            // 1 (y 20..30) over 2 (y 12..22) over 3 (y 0..14). Pairs (1,2) then (2,3), Move Down.
            var boxes = new Dictionary<long, ResolveBox>
            {
                [1] = Box(1, 0, 20), [2] = Box(2, 0, 12), [3] = Box(3, 0, 0, 40, 14),
            };
            var pairs = new[] { (1L, 2L), (2L, 3L) };

            var plan = OverlapResolver.Plan(boxes, pairs, ResolveMode.MoveDown, 0, null, null, Avoid());

            Assert.All(plan.Pairs, p => Assert.NotEqual(PairOutcome.Skipped, p.Outcome));
            AssertNoOverlaps(boxes, plan);
        }

        [Fact]
        public void PinnedNeighbour_IsAnObstacle_NotABlock()
        {
            var plan = OverlapResolver.Plan(PairWithNeighbourAbove(), OnePair, ResolveMode.MoveUp, 1.0,
                id => id == 3 ? "pinned" : null, null, Avoid());

            Assert.Equal(PairOutcome.Planned, plan.Pairs[0].Outcome);
            Assert.Equal(21.0, plan.MovesMm[1], 6);
        }

        [Fact]
        public void InvalidTolerance_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                OverlapResolver.Plan(PairWithNeighbourAbove(), OnePair, ResolveMode.MoveUp, 1.0, null, null, Avoid(-1)));
        }

        [Fact]
        public void Near_WithZeroTolerance_MatchesIntersect()
        {
            var a = Box(1, 0, 0, 10, 10);
            var touching = Box(2, 0, 10, 10, 10);
            Assert.False(OverlapResolver.Near(a.X, a.Y, a.Width, a.Height, touching, 0));
            Assert.True(OverlapResolver.Near(a.X, a.Y, a.Width, a.Height, touching, 0.5));
        }
    }
}
