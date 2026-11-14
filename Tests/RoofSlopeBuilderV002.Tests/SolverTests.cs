using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Core;
using Xunit;

namespace RoofSlopeBuilderV002.Tests
{
    public class SolverTests
    {
        private const double Mm = 1 / 304.8;

        private static List<P2> Rect(double x0, double y0, double x1, double y1) =>
            new List<P2> { new P2(x0, y0), new P2(x1, y0), new P2(x1, y1), new P2(x0, y1) };

        private static DrainInfo Drain(List<P2> outline, int loop)
        {
            P2 c = Geo2D.Centroid(outline);
            return new DrainInfo
            {
                LoopIndex = loop, Center = c, Area = Geo2D.Area(outline),
                Radius = outline.Max(p => p.DistanceTo(c)), Outline = outline, Selected = true
            };
        }

        /// <summary>20 m x 10 m roof with drains of 300 x 300 mm.</summary>
        private static RoofInput Roof(params P2[] drainCentres)
        {
            RoofInput r = new RoofInput { Outer = Rect(0, 0, 20000 * Mm, 10000 * Mm) };
            foreach (P2 c in drainCentres)
            {
                List<P2> o = Rect(c.X - 150 * Mm, c.Y - 150 * Mm, c.X + 150 * Mm, c.Y + 150 * Mm);
                r.Inner.Add(o);
                r.InnerCorners.Add(o);
                r.Drains.Add(Drain(o, r.Inner.Count - 1));
            }
            return r;
        }

        [Fact]
        public void Height_is_measured_from_the_drain_edge_not_a_circle()
        {
            // A long thin drain: 600 x 150 mm. A point 200 mm off its long side used to sit inside the
            // circle round the centre (radius ~309 mm) and got height 0.
            List<P2> o = Rect(-300 * Mm, -75 * Mm, 300 * Mm, 75 * Mm);
            DrainInfo d = Drain(o, 0);
            Assert.Equal(200, d.DistanceTo(new P2(0, 275 * Mm)) / Mm, 3);
            Assert.Equal(0, d.DistanceTo(new P2(0, 0)), 9);
        }

        [Fact]
        public void Corner_far_from_a_single_drain_gets_slope_times_distance()
        {
            RoofInput roof = Roof(new P2(10000 * Mm, 5000 * Mm));
            RoofSlopeSettings s = new RoofSlopeSettings { RoundHeightTo = 0 };
            SolveResult res = SlopeSolver.Solve(roof, s);
            Assert.False(res.Skipped);
            double h = res.Field.At(new P2(10000 * Mm, 0)) / Mm;  // 4850 mm from the drain's lower side
            Assert.Equal(4850 * 0.015, h, 3);
        }

        [Fact]
        public void Two_drains_give_a_ridge_halfway_between_them()
        {
            RoofInput roof = Roof(new P2(5000 * Mm, 5000 * Mm), new P2(15000 * Mm, 5000 * Mm));
            SolveResult res = SlopeSolver.Solve(roof, new RoofSlopeSettings());
            Assert.True(res.RidgePointCount > 0);
            Assert.All(res.Points.Where(p => p.Kind == PointKind.Ridge && p.P.Y > 1 && p.P.Y < 9000 * Mm - 1),
                p => Assert.Equal(10000, p.P.X / Mm, 1));
            Assert.NotEmpty(res.SplitLines);
        }

        [Fact]
        public void No_drain_selected_skips_the_roof()
        {
            RoofInput roof = Roof(new P2(10000 * Mm, 5000 * Mm));
            roof.Drains[0].Selected = false;
            Assert.True(SlopeSolver.Solve(roof, new RoofSlopeSettings()).Skipped);
        }

        [Fact]
        public void Points_are_never_closer_than_the_merge_tolerance()
        {
            RoofInput roof = Roof(new P2(5000 * Mm, 5000 * Mm), new P2(15000 * Mm, 5000 * Mm), new P2(10000 * Mm, 8000 * Mm));
            RoofSlopeSettings s = new RoofSlopeSettings();
            SolveResult res = SlopeSolver.Solve(roof, s);
            for (int i = 0; i < res.Points.Count; i++)
                for (int j = i + 1; j < res.Points.Count; j++)
                    Assert.True(res.Points[i].P.DistanceTo(res.Points[j].P) >= s.MergeTolerance - 1e-9);
        }

        [Fact]
        public void Default_detection_keeps_only_the_smallest_size()
        {
            List<DrainInfo> d = new List<DrainInfo>
            {
                new DrainInfo { Area = 0.09 * 10.7639104 }, new DrainInfo { Area = 0.0905 * 10.7639104 },
                new DrainInfo { Area = 0.2 * 10.7639104 }, new DrainInfo { Area = 1.0 * 10.7639104 }
            };
            DrainDetector.ApplyDefaultSelection(d, new RoofSlopeSettings());
            Assert.Equal(new[] { true, true, false, false }, d.Select(x => x.Selected).ToArray());
        }

        [Fact]
        public void Settings_round_trip_through_json_with_version()
        {
            RoofSlopeSettings s = new RoofSlopeSettings { Slope = 0.02, RidgeMode = RidgeMode.Saddle, SlopeUnitIndex = 1 };
            RoofSlopeSettings back = JsonSerializer.Deserialize<RoofSlopeSettings>(JsonSerializer.Serialize(s));
            Assert.Equal(0.02, back.Slope);
            Assert.Equal(RidgeMode.Saddle, back.RidgeMode);
            Assert.Equal(1, back.SlopeUnitIndex);
            Assert.Equal(RoofSlopeSettings.CurrentVersion, back.SettingsVersion);
        }

        [Fact]
        public void Old_settings_file_without_version_still_loads()
        {
            RoofSlopeSettings back = JsonSerializer.Deserialize<RoofSlopeSettings>("{\"Slope\":0.025}");
            Assert.Equal(0.025, back.Slope);
            Assert.True(back.AddDrainRidges);
        }
    }
}
