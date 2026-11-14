using System;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;
using static VoidOpenings.Tests.TestData;

namespace VoidOpenings.Tests
{
    public class ClashEngineTests
    {
        private static readonly ClashEngine Engine = new ClashEngine();

        [Fact]
        public void Horizontal_pipe_through_a_wall_gives_one_clash_with_entry_and_exit()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));

            var clashes = Engine.Detect(Structure(Wall("W1")), Mep(pipe));

            var clash = Assert.Single(clashes);
            AssertNear(new Vec3(0, 500, 1500), clash.EntryPoint);
            AssertNear(new Vec3(200, 500, 1500), clash.ExitPoint);
            AssertNear(Vec3.UnitX, clash.MepDirection);
            Assert.False(clash.IsPartial);
            Assert.False(clash.NeedsReview);
            Assert.Equal("W1", clash.Structural.UniqueId);
            Assert.Equal("P1", clash.Mep.UniqueId);
        }

        [Fact]
        public void Pipe_that_passes_beside_the_wall_gives_no_clash()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 1100, 1500), new Vec3(700, 1100, 1500));

            Assert.Empty(Engine.Detect(Structure(Wall("W1")), Mep(pipe)));
        }

        [Fact]
        public void Overlapping_bounding_boxes_alone_are_not_a_clash()
        {
            // Diagonal pipe: its box overlaps the wall's box, but the pipe passes the wall's end 400 mm away.
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-800, 2000, 1500), new Vec3(1200, 800, 1500));

            Assert.Empty(Engine.Detect(Structure(Wall("W1")), Mep(pipe)));
        }

        [Fact]
        public void Vertical_pipe_through_a_slab_gives_one_clash()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(300, 0, -500), new Vec3(300, 0, 900));

            var clash = Assert.Single(Engine.Detect(Structure(Slab("F1")), Mep(pipe)));

            AssertNear(new Vec3(300, 0, 0), clash.EntryPoint);
            AssertNear(new Vec3(300, 0, 200), clash.ExitPoint);
            AssertNear(Vec3.UnitZ, clash.MepDirection);
        }

        [Fact]
        public void Sloped_pipe_through_a_slab_keeps_its_real_direction()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 0, -500), new Vec3(700, 0, 700));

            var clash = Assert.Single(Engine.Detect(Structure(Slab("F1")), Mep(pipe)));

            AssertNear(new Vec3(0, 0, 0), clash.EntryPoint);
            AssertNear(new Vec3(200, 0, 200), clash.ExitPoint);
            AssertNear(new Vec3(1, 0, 1).Normalized(), clash.MepDirection);
        }

        [Fact]
        public void One_pipe_through_three_walls_gives_three_clashes_in_order_along_the_pipe()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(5000, 500, 1500));
            var structure = Structure(Wall("W3", 4000), Wall("W1", 0), Wall("W2", 2000));

            var clashes = Engine.Detect(structure, Mep(pipe));

            Assert.Equal(new[] { "W1", "W2", "W3" }, clashes.Select(c => c.Structural.UniqueId).ToArray());
        }

        [Fact]
        public void Two_penetrations_of_the_same_structural_element_get_ordinals_0_and_1()
        {
            var tris = BoxTriangles(new Vec3(0, 0, 0), new Vec3(200, 1000, 3000));
            tris.AddRange(BoxTriangles(new Vec3(2000, 0, 0), new Vec3(2200, 1000, 3000)));
            var uShaped = new StructuralElementData("W1", 100, StructuralCategory.Wall, new TriangleMesh(tris));
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(3000, 500, 1500));

            var clashes = Engine.Detect(Structure(uShaped), Mep(pipe));

            Assert.Equal(new[] { 0, 1 }, clashes.Select(c => c.Key.Ordinal).ToArray());
            Assert.NotEqual(clashes[0].ClashId, clashes[1].ClashId);
        }

        [Fact]
        public void Segments_that_join_inside_the_wall_give_one_clash_not_two()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100,
                new Vec3(-500, 500, 1500), new Vec3(100, 500, 1500), new Vec3(700, 500, 1500));

            var clash = Assert.Single(Engine.Detect(Structure(Wall("W1")), Mep(pipe)));

            AssertNear(new Vec3(0, 500, 1500), clash.EntryPoint);
            AssertNear(new Vec3(200, 500, 1500), clash.ExitPoint);
            Assert.False(clash.IsPartial);
        }

        [Fact]
        public void Pipe_that_ends_inside_the_wall_is_a_partial_clash_for_review()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(100, 500, 1500));

            var clash = Assert.Single(Engine.Detect(Structure(Wall("W1")), Mep(pipe)));

            Assert.True(clash.IsPartial);
            Assert.True(clash.NeedsReview);
            AssertNear(new Vec3(100, 500, 1500), clash.ExitPoint);
        }

        [Fact]
        public void Pipe_whose_body_clips_the_wall_end_while_its_centreline_misses_is_flagged_for_review()
        {
            // Wall ends at y = 1000. Centreline at y = 1030, radius 50: the body cuts 20 mm into the wall.
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 1030, 1500), new Vec3(700, 1030, 1500));

            var clash = Assert.Single(Engine.Detect(Structure(Wall("W1")), Mep(pipe)));

            Assert.True(clash.NeedsReview);
            AssertNear(new Vec3(0, 1030, 1500), clash.EntryPoint);
            AssertNear(new Vec3(200, 1030, 1500), clash.ExitPoint);
        }

        [Fact]
        public void Clash_id_is_the_same_on_every_run_and_does_not_depend_on_element_ids()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));

            string first = Engine.Detect(Structure(Wall("W1")), Mep(pipe)).Single().ClashId;
            string second = Engine.Detect(Structure(Wall("W1")), Mep(pipe)).Single().ClashId;
            string otherWall = Engine.Detect(Structure(Wall("W9")), Mep(pipe)).Single().ClashId;

            Assert.Equal(first, second);
            Assert.NotEqual(first, otherWall);
            Assert.StartsWith("CL-", first);
        }

        [Fact]
        public void Cancelled_token_stops_detection()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(() => Engine.Detect(Structure(Wall("W1")), Mep(pipe), null, cts.Token));
        }

        [Fact]
        public void Progress_reaches_one_when_detection_finishes()
        {
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));
            var progress = new LastValue();

            Engine.Detect(Structure(Wall("W1")), Mep(pipe), progress, CancellationToken.None);

            Assert.Equal(1.0, progress.Value, 6);
        }

        private sealed class LastValue : IProgress<double>
        {
            public double Value { get; private set; }

            public void Report(double value) => Value = value;
        }
    }
}
