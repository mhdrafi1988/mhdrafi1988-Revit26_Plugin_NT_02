using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core;
using Xunit;

namespace RoofSlopeBuilderV002.Tests
{
    /// <summary>V002: ridge lines and ridge points to draw.</summary>
    public class RidgeTests
    {
        private const double Mm = 1 / 304.8;

        private static List<P2> Rect(double x0, double y0, double x1, double y1) =>
            new List<P2> { new P2(x0, y0), new P2(x1, y0), new P2(x1, y1), new P2(x0, y1) };

        /// <summary>20 m x 10 m roof with 300 x 300 mm drains at the given centres (mm).</summary>
        private static RoofInput Roof(params (double x, double y)[] drainsMm)
        {
            RoofInput r = new RoofInput { Outer = Rect(0, 0, 20000 * Mm, 10000 * Mm) };
            foreach (var (x, y) in drainsMm)
            {
                P2 c = new P2(x * Mm, y * Mm);
                List<P2> o = Rect(c.X - 150 * Mm, c.Y - 150 * Mm, c.X + 150 * Mm, c.Y + 150 * Mm);
                r.Inner.Add(o);
                r.InnerCorners.Add(o);
                r.Drains.Add(new DrainInfo
                {
                    LoopIndex = r.Inner.Count - 1, Center = c, Area = Geo2D.Area(o),
                    Radius = o.Max(p => p.DistanceTo(c)), Outline = o, Selected = true
                });
            }
            return r;
        }

        [Fact]
        public void Join_merges_touching_segments_and_stops_at_junctions()
        {
            // a T: two collinear pieces along y = 0 meeting a stem at (1, 0)
            var segs = new[]
            {
                new[] { new P2(0, 0), new P2(1, 0) },
                new[] { new P2(1, 0), new P2(2, 0) },
                new[] { new P2(1, 0), new P2(1, 1) },
            };
            List<List<P2>> chains = RidgeGeometry.Join(segs, 1e-6);
            Assert.Equal(3, chains.Count);                       // the junction splits all three
            var straight = RidgeGeometry.Join(segs.Take(2), 1e-6);
            Assert.Single(straight);
            Assert.Equal(2, straight[0].Count);                  // collinear middle point dropped
        }

        [Fact]
        public void Sample_includes_ends_and_respects_spacing()
        {
            var line = new List<P2> { new P2(0, 0), new P2(10, 0) };
            List<P2> pts = RidgeGeometry.Sample(new[] { line }, 3, 0.01);
            Assert.Contains(pts, p => p.DistanceTo(new P2(0, 0)) < 1e-9);
            Assert.Contains(pts, p => p.DistanceTo(new P2(10, 0)) < 1e-9);
            Assert.Equal(5, pts.Count);                          // 10 / 3 -> 4 gaps of 2.5
        }

        [Fact]
        public void Two_drains_give_one_ridge_line_across_the_roof()
        {
            SolveResult res = SlopeSolver.Solve(Roof((5000, 5000), (15000, 5000)), new RoofSlopeSettings());
            List<P2> line = Assert.Single(res.RidgeLines);
            Assert.All(line, p => Assert.Equal(10000, p.X / Mm, 1));
            Assert.Equal(10000, line.First().DistanceTo(line.Last()) / Mm, 1);
            // ridge points: both ends plus points no farther apart than 1500 mm
            Assert.True(res.RidgeMarks.Count >= 1 + 10000 / 1500);
        }

        [Fact]
        public void Three_drains_give_ridges_meeting_at_one_point()
        {
            SolveResult res = SlopeSolver.Solve(Roof((5000, 3000), (15000, 3000), (10000, 8000)), new RoofSlopeSettings());
            Assert.Equal(3, res.RidgeLines.Count);
            // every line has one end at the shared Voronoi vertex
            P2 hub = res.RidgeLines[0].First();
            if (!res.RidgeLines.Skip(1).All(l => l.First().DistanceTo(hub) < 1e-3 || l.Last().DistanceTo(hub) < 1e-3))
                hub = res.RidgeLines[0].Last();
            Assert.All(res.RidgeLines, l => Assert.True(l.First().DistanceTo(hub) < 1e-3 || l.Last().DistanceTo(hub) < 1e-3));
        }

        [Fact]
        public void Ridges_are_found_even_when_ridge_points_are_not_added()
        {
            RoofSlopeSettings s = new RoofSlopeSettings { AddDrainRidges = false };
            SolveResult res = SlopeSolver.Solve(Roof((5000, 5000), (15000, 5000)), s);
            Assert.Single(res.RidgeLines);
            Assert.Equal(0, res.RidgePointCount);                // no shape points were added for them
        }

        [Fact]
        public void Saddle_mode_gives_a_crest_across_the_drain_line()
        {
            RoofSlopeSettings s = new RoofSlopeSettings { RidgeMode = RidgeMode.Saddle };
            SolveResult res = SlopeSolver.Solve(Roof((5000, 5000), (15000, 5000)), s);
            List<P2> crest = Assert.Single(res.RidgeLines);
            Assert.Equal(3, crest.Count);                         // tip, peak, tip
            Assert.All(crest, p => Assert.Equal(10000, p.X / Mm, 1));
            Assert.Equal(10000.0 / 3, crest.First().DistanceTo(crest.Last()) / Mm, 0);   // 1/3 of the spacing
            Assert.Equal(3, res.RidgeMarks.Count);
        }

        [Fact]
        public void One_drain_has_no_ridges()
        {
            SolveResult res = SlopeSolver.Solve(Roof((10000, 5000)), new RoofSlopeSettings());
            Assert.Empty(res.RidgeLines);
            Assert.Empty(res.RidgeMarks);
        }

        [Fact]
        public void Version_1_settings_load_with_ridge_drawing_off()
        {
            RoofSlopeSettings s = JsonSerializer.Deserialize<RoofSlopeSettings>("{\"SettingsVersion\":1,\"Slope\":0.02}");
            Assert.Equal(0.02, s.Slope);
            Assert.False(s.DrawRidgeLines);
            Assert.False(s.MarkRidgePoints);
            Assert.Equal(RidgeLineKind.DetailLine, s.RidgeLineKind);
            Assert.Equal(1, s.SettingsVersion);                   // SettingsStore stamps it to 2 on load
        }
    }
}
