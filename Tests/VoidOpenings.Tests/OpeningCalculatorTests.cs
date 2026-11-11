using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;
using static VoidOpenings.Tests.TestData;

namespace VoidOpenings.Tests
{
    public class OpeningCalculatorTests
    {
        private static ClearanceRuleSet Rules() => new ClearanceRuleSet(new[]
        {
            new ClearanceRule(MepCategory.Conduit, 0, 25, 25, ClearanceMode.PerSide),
            new ClearanceRule(MepCategory.Conduit, 26, 50, 30, ClearanceMode.PerSide),
            new ClearanceRule(MepCategory.Pipe, 0, 50, 25, ClearanceMode.PerSide),
            new ClearanceRule(MepCategory.Pipe, 51, 100, 40, ClearanceMode.PerSide),
            new ClearanceRule(MepCategory.Duct, 0, 1000, 75, ClearanceMode.Total),
        }, defaultClearanceMm: 50, defaultMode: ClearanceMode.PerSide);

        private static OpeningSettings Settings() => new OpeningSettings { OvershootMm = 50, RoundUpToMm = 0 };

        private static ClashData ThroughWall(MepElementData mep)
            => new ClashEngine().Detect(Structure(Wall("W1")), Mep(mep)).Single();

        private static MepElementData RoundThroughWall(MepCategory category, double diameter)
            => Round("M1", category, diameter, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));

        [Fact]
        public void Conduit_40_with_30_each_side_needs_a_100_opening()
        {
            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(RoundThroughWall(MepCategory.Conduit, 40)));

            Assert.Equal(OpeningShape.Circular, opening.Shape);
            Assert.Equal(100, opening.DiameterMm, 3);
            Assert.Equal(30, opening.ClearanceMm, 3);
        }

        [Fact]
        public void Total_mode_adds_the_clearance_once()
        {
            var rules = new ClearanceRuleSet(new[] { new ClearanceRule(MepCategory.Conduit, 0, 50, 30, ClearanceMode.Total) }, 50, ClearanceMode.PerSide);

            var opening = new OpeningCalculator(rules, Settings()).Calculate(ThroughWall(RoundThroughWall(MepCategory.Conduit, 40)));

            Assert.Equal(70, opening.DiameterMm, 3);
        }

        [Fact]
        public void Size_outside_every_rule_uses_the_default_clearance()
        {
            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(RoundThroughWall(MepCategory.Pipe, 150)));

            Assert.Equal(250, opening.DiameterMm, 3);
            Assert.Equal(50, opening.ClearanceMm, 3);
        }

        [Fact]
        public void Fitting_without_its_own_rule_uses_the_rule_of_its_base_category()
        {
            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(RoundThroughWall(MepCategory.ConduitFitting, 40)));

            Assert.Equal(100, opening.DiameterMm, 3);
        }

        [Fact]
        public void Opening_through_a_wall_is_centred_in_it_and_deep_enough_to_pass_through()
        {
            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(RoundThroughWall(MepCategory.Pipe, 100)));

            AssertNear(new Vec3(100, 500, 1500), opening.Center);
            AssertNear(Vec3.UnitX, opening.Orientation.AxisZ);
            Assert.Equal(300, opening.DepthMm, 3); // 200 wall + 50 overshoot each side
            Assert.Equal(OpeningStatus.Detected, opening.Status);
        }

        [Fact]
        public void Insulation_is_added_to_the_size_but_the_rule_is_picked_by_nominal_size()
        {
            var pipe = RoundThroughWall(MepCategory.Pipe, 100);
            pipe.InsulationMm = 25;

            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(pipe));

            Assert.Equal(230, opening.DiameterMm, 3); // 100 + 2x25 insulation + 2x40
        }

        [Fact]
        public void Insulation_is_ignored_when_the_option_is_off()
        {
            var pipe = RoundThroughWall(MepCategory.Pipe, 100);
            pipe.InsulationMm = 25;
            var settings = Settings();
            settings.IncludeInsulation = false;

            var opening = new OpeningCalculator(Rules(), settings).Calculate(ThroughWall(pipe));

            Assert.Equal(180, opening.DiameterMm, 3);
        }

        [Fact]
        public void Rectangular_duct_gets_a_rectangular_opening_rounded_up_to_the_module()
        {
            var duct = new MepElementData("D1", 300, MepCategory.Duct, new[]
            {
                new MepSegment(new Segment3(new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500)), MepProfile.Rectangular(600, 300), Vec3.UnitY),
            });
            var settings = Settings();
            settings.RoundUpToMm = 10;

            var opening = new OpeningCalculator(Rules(), settings).Calculate(ThroughWall(duct));

            Assert.Equal(OpeningShape.Rectangular, opening.Shape);
            Assert.Equal(680, opening.WidthMm, 3);  // 600 + 75 = 675, rounded up
            Assert.Equal(380, opening.HeightMm, 3); // 300 + 75 = 375, rounded up
            Assert.Equal(1.0, System.Math.Abs(opening.Orientation.AxisX.Dot(Vec3.UnitY)), 3);
        }

        [Fact]
        public void Sloped_pipe_through_a_slab_gets_a_perpendicular_rectangular_opening_covering_both_faces()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 0, -500), new Vec3(700, 0, 700));
            var clash = new ClashEngine().Detect(Structure(Slab("F1")), Mep(pipe)).Single();

            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(clash);

            Assert.Equal(OpeningShape.Rectangular, opening.Shape);
            AssertNear(Vec3.UnitZ, opening.Orientation.AxisZ);
            AssertNear(Vec3.UnitX, opening.Orientation.AxisX);
            Assert.Equal(421.42, opening.WidthMm, 2);  // 100/cos45 + 200*tan45 + 2x40
            Assert.Equal(180, opening.HeightMm, 3);    // 100 + 2x40
            Assert.Equal(300, opening.DepthMm, 3);
            AssertNear(new Vec3(100, 0, 100), opening.Center);
        }

        [Fact]
        public void Slightly_sloped_pipe_keeps_a_circular_opening_enlarged_to_cover_the_slope()
        {
            // 1:10 slope through the 200 wall: about 5.7 degrees, under the 15 degree limit.
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-1000, 500, 1400), new Vec3(1000, 500, 1600));

            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(pipe));

            Assert.Equal(OpeningShape.Circular, opening.Shape);
            Assert.Equal(200.50, opening.DiameterMm, 2); // 100/cos + 200*tan = 120.50, + 2x40
        }

        [Fact]
        public void Follow_mep_option_points_the_opening_along_the_pipe()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 0, -500), new Vec3(700, 0, 700));
            var clash = new ClashEngine().Detect(Structure(Slab("F1")), Mep(pipe)).Single();
            var settings = Settings();
            settings.AxisMode = OpeningAxisMode.FollowMep;

            var opening = new OpeningCalculator(Rules(), settings).Calculate(clash);

            Assert.Equal(OpeningShape.Circular, opening.Shape);
            Assert.Equal(180, opening.DiameterMm, 3);
            AssertNear(new Vec3(1, 0, 1).Normalized(), opening.Orientation.AxisZ);
        }

        [Fact]
        public void Pipe_through_a_beam_gets_an_opening_perpendicular_to_the_beam_axis()
        {
            // Beam along Y; pipe crosses it sloping in the X-Y plane. The opening axis must have no Y part.
            var beam = new StructuralElementData("B1", 120, StructuralCategory.StructuralFraming,
                Box(new Vec3(0, -3000, 0), new Vec3(300, 3000, 600))) { MemberAxis = Vec3.UnitY };
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, -50, 300), new Vec3(800, 80, 300));
            var clash = new ClashEngine().Detect(Structure(beam), Mep(pipe)).Single();

            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(clash);

            AssertNear(Vec3.UnitX, opening.Orientation.AxisZ);
        }

        [Fact]
        public void Clash_flagged_for_review_gives_an_opening_with_review_status()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(100, 500, 1500));

            var opening = new OpeningCalculator(Rules(), Settings()).Calculate(ThroughWall(pipe));

            Assert.Equal(OpeningStatus.Review, opening.Status);
        }

        [Fact]
        public void Rule_can_force_a_rectangular_opening_for_a_round_pipe()
        {
            var rules = new ClearanceRuleSet(new[]
            {
                new ClearanceRule(MepCategory.Pipe, 0, 200, 40, ClearanceMode.PerSide, OpeningShapePolicy.Rectangular),
            }, 50, ClearanceMode.PerSide);

            var opening = new OpeningCalculator(rules, Settings()).Calculate(ThroughWall(RoundThroughWall(MepCategory.Pipe, 100)));

            Assert.Equal(OpeningShape.Rectangular, opening.Shape);
            Assert.Equal(180, opening.WidthMm, 3);
            Assert.Equal(180, opening.HeightMm, 3);
        }
    }
}
