using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services;
using System.Collections.Generic;
using Xunit;

namespace AlignSpotElevations.Tests
{
    /// <summary>Align to detail lines: reach checks and plans.</summary>
    public class DetailLineTests
    {
        private static DetailLine2D L(double au, double av, double bu, double bv) => new DetailLine2D { AU = au, AV = av, BU = bu, BV = bv };

        private static AlignOptions Opt() => new AlignOptions { Mode = ArrangeMode.AlignToDetailLines };

        /// <summary>Spot with a straight leader (no bend): point, end and text.</summary>
        private static SpotItem Straight(long id, double eu, double ev)
            => new SpotItem { Id = id, HasEnd = true, EndU = eu, EndV = ev, U = eu + 3, V = ev, PointU = eu - 10, PointV = ev - 10 };

        /// <summary>Spot with a bend: horizontal bend-to-end segment from (su, v) to (su + 10, v).</summary>
        private static SpotItem Bent(long id, double su, double v)
            => new SpotItem
            {
                Id = id, HasEnd = true, HasShoulder = true, ShoulderU = su, ShoulderV = v,
                EndU = su + 10, EndV = v, U = su + 13, V = v, PointU = su - 10, PointV = v - 10
            };

        [Fact]
        public void Vertical_SlidesOntoSlopedLine_KeepsX()
        {
            Assert.True(AlignEngine.TryReach(20, 0, L(0, 0, 100, 50), AlignDirection.Vertical, false, out double u, out double v, out _));
            Assert.Equal(20, u, 9);
            Assert.Equal(10, v, 9);
        }

        [Fact]
        public void Horizontal_SlidesOntoSlopedLine_KeepsY()
        {
            Assert.True(AlignEngine.TryReach(0, 25, L(0, 0, 100, 50), AlignDirection.Horizontal, false, out double u, out double v, out _));
            Assert.Equal(50, u, 9);
            Assert.Equal(25, v, 9);
        }

        [Fact]
        public void Vertical_OntoVerticalLine_NotPossible()
        {
            Assert.False(AlignEngine.TryReach(5, 5, L(10, 0, 10, 100), AlignDirection.Vertical, true, out _, out _, out string why));
            Assert.Equal("line is vertical", why);
        }

        [Fact]
        public void Horizontal_OntoHorizontalLine_NotPossible()
        {
            Assert.False(AlignEngine.TryReach(5, 5, L(0, 10, 100, 10), AlignDirection.Horizontal, true, out _, out _, out string why));
            Assert.Equal("line is horizontal", why);
        }

        [Fact]
        public void PastTheEnd_SkippedByDefault_ReachedWhenExtended()
        {
            DetailLine2D line = L(10, 50, 50, 50); // 40 mm horizontal line starting at U = 10
            Assert.False(AlignEngine.TryReach(0, 0, line, AlignDirection.Vertical, false, out _, out _, out string why));
            Assert.Equal("past the line end", why);
            Assert.True(AlignEngine.TryReach(0, 0, line, AlignDirection.Vertical, true, out double u, out double v, out _));
            Assert.Equal(0, u, 9);
            Assert.Equal(50, v, 9);
        }

        [Fact]
        public void EndLine_Auto_UsesShorterMove_TextFollows()
        {
            var o = Opt();
            o.EndLine = L(0, 0, 100, 100); // 45°
            var items = new List<SpotItem> { Straight(1, 30, 20) };
            ArrangePlan plan = AlignEngine.Plan(items, o);
            PlannedMove m = plan.Entries[0];
            // Vertical: (30,30), 10 mm; horizontal: (20,20), 10 mm; equal, vertical tried first.
            Assert.True(m.SetsEnd);
            Assert.Equal(0, m.EndDeltaU, 9);
            Assert.Equal(10, m.EndDeltaV, 9);
            Assert.False(m.SetsText);
            Assert.Equal(m.EndDeltaV, m.DeltaV, 9); // text follows the end
        }

        [Fact]
        public void EndLine_Horizontal_ForcedDirection()
        {
            var o = Opt();
            o.EndLine = L(0, 0, 100, 100);
            o.EndLineMove = LineMove.Horizontal;
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { Straight(1, 30, 20) }, o);
            Assert.Equal(-10, plan.Entries[0].EndDeltaU, 9);
            Assert.Equal(0, plan.Entries[0].EndDeltaV, 9);
            Assert.Contains("end: moved horizontally", plan.Entries[0].LineResult);
        }

        [Fact]
        public void EndWithBend_Auto_PrefersDirectionItCanSlide()
        {
            // Horizontal bend-to-end segment: a vertical move cannot happen, so Auto goes horizontal.
            var o = Opt();
            o.EndLine = L(0, -40, 100, 60); // sloped: at V = 20, U = 60
            SpotItem s = Bent(1, 20, 20);   // end at (30, 20)
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { s }, o);
            PlannedMove m = plan.Entries[0];
            Assert.False(m.EndLimited);
            Assert.Equal(30, m.EndDeltaU, 9);
            Assert.Equal(0, m.EndDeltaV, 9);
        }

        [Fact]
        public void EndWithBend_ForcedVertical_CannotSlide()
        {
            var o = Opt();
            o.EndLine = L(0, -40, 100, 60);
            o.EndLineMove = LineMove.Vertical;
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { Bent(1, 20, 20) }, o);
            Assert.True(plan.Entries[0].EndLimited);
            Assert.Equal(1, plan.EndLimitedCount);
            Assert.Contains("cannot slide onto the line", plan.Entries[0].LineResult);
        }

        [Fact]
        public void BendThenEnd_EndStartsFromMovedBend()
        {
            var o = Opt();
            o.ShoulderLine = L(0, 50, 100, 50); // horizontal at V = 50
            o.EndLine = L(70, 0, 70, 100);       // vertical at U = 70
            SpotItem s = Bent(1, 20, 20);        // bend (20,20), end (30,20)
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { s }, o);
            PlannedMove m = plan.Entries[0];
            Assert.Equal(0, m.ShoulderDeltaU, 9);
            Assert.Equal(30, m.ShoulderDeltaV, 9);   // bend to (20,50)
            Assert.Equal(40, m.EndDeltaU, 9);        // end to (70,50)
            Assert.Equal(30, m.EndDeltaV, 9);
            Assert.False(m.EndLimited);
        }

        [Fact]
        public void TextLine_MovesTextOnly_SpotWithoutLeaderToo()
        {
            var o = Opt();
            o.TextLine = L(100, 0, 100, 100);
            var noLeader = new SpotItem { Id = 2, U = 40, V = 30 };
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { Straight(1, 30, 20), noLeader }, o);
            Assert.True(plan.Entries[0].SetsText);
            Assert.False(plan.Entries[0].SetsEnd);
            Assert.Equal(67, plan.Entries[0].DeltaU, 9); // text at 33 -> 100
            Assert.Equal(60, plan.Entries[1].DeltaU, 9);
        }

        [Fact]
        public void MissingParts_AndSkips_AreCounted()
        {
            var o = Opt();
            o.EndLine = L(0, 0, 10, 0); // short horizontal line
            var far = Straight(1, 50, 20);              // past the end vertically
            var none = new SpotItem { Id = 2, U = 5, V = 5 }; // no leader
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { far, none }, o);
            Assert.Equal(1, plan.LineSkippedCount);
            Assert.Equal(1, plan.MissingEndCount);
            Assert.Equal(0, plan.MoveCount);
            Assert.Contains("end: skipped, past the line end", plan.Entries[0].LineResult);
            Assert.Contains("end: no leader", plan.Entries[1].LineResult);
        }

        [Fact]
        public void CountReachable_CountsOnlySpotsWithThePart()
        {
            var items = new List<SpotItem> { Straight(1, 5, 0), Straight(2, 50, 0), new SpotItem { Id = 3 } };
            DetailLine2D line = L(0, 10, 20, 10);
            Assert.Equal(1, AlignEngine.CountReachable(items, SpotPart.LeaderEnd, line, AlignDirection.Vertical, false));
            Assert.Equal(2, AlignEngine.CountReachable(items, SpotPart.LeaderEnd, line, AlignDirection.Vertical, true));
            Assert.Equal(0, AlignEngine.CountReachable(items, SpotPart.LeaderEnd, line, AlignDirection.Horizontal, true));
        }

        [Fact]
        public void NoLines_NothingMoves()
        {
            ArrangePlan plan = AlignEngine.Plan(new List<SpotItem> { Straight(1, 1, 1), Straight(2, 2, 2) }, Opt());
            Assert.Equal(0, plan.MoveCount);
        }

        [Fact]
        public void Describe_NamesShapeAndLength()
        {
            Assert.Equal("horizontal · 40 mm", L(0, 0, 40, 0).Describe());
            Assert.Equal("vertical · 30 mm", L(5, 0, 5, 30).Describe());
            Assert.Equal("sloped, 45° · 14 mm", L(0, 0, 10, 10).Describe());
        }
    }
}
