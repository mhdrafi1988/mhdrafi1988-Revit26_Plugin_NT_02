using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;

namespace VoidOpenings.Tests
{
    public class AnalyzeTests
    {
        private sealed class FakeGeometry : IGeometryReader
        {
            public List<MepElementData> Mep = new List<MepElementData>();
            public List<StructuralElementData> Structural = new List<StructuralElementData>();
            public List<SkippedElement> SkippedMep = new List<SkippedElement>();
            public Aabb? RegionAsked;
            public List<int> MepBatches = new List<int>();
            public List<int> StructuralBatches = new List<int>();
            public Action OnStructuralBatch;

            public IReadOnlyList<long> ListMep(ModelRef model, IReadOnlyList<MepCategory> categories)
                => Mep.Where(e => categories.Contains(e.Category)).Select(e => e.ElementId).ToList();

            public GeometryBatch<MepElementData> ReadMep(ModelRef model, IReadOnlyList<long> ids)
            {
                MepBatches.Add(ids.Count);
                return new GeometryBatch<MepElementData>(Mep.Where(e => ids.Contains(e.ElementId)).ToList(),
                    MepBatches.Count == 1 ? SkippedMep : new List<SkippedElement>());
            }

            public IReadOnlyList<long> ListStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories, Aabb? regionMm)
            {
                RegionAsked = regionMm;
                return Structural.Where(e => categories.Contains(e.Category))
                    .Where(e => !regionMm.HasValue || regionMm.Value.Intersects(e.Bounds))
                    .Select(e => e.ElementId).ToList();
            }

            public GeometryBatch<StructuralElementData> ReadStructural(ModelRef model, IReadOnlyList<long> ids)
            {
                StructuralBatches.Add(ids.Count);
                OnStructuralBatch?.Invoke();
                return new GeometryBatch<StructuralElementData>(Structural.Where(e => ids.Contains(e.ElementId)).ToList(), null);
            }
        }

        private sealed class InlineDispatcher : IRevitDispatcher
        {
            public Task<T> RunAsync<T>(Func<T> work)
            {
                try { return Task.FromResult(work()); }
                catch (Exception ex) { return Task.FromException<T>(ex); }
            }
        }

        private sealed class Collect : IProgress<AnalysisProgress>
        {
            public readonly List<AnalysisProgress> Reports = new List<AnalysisProgress>();

            public void Report(AnalysisProgress value) { lock (Reports) Reports.Add(value); }
        }

        private static MepElementData Conduit(long id, double y, double diameter = 40)
        {
            MepElementData template = TestData.Round("conduit-" + id, MepCategory.Conduit, diameter, new Vec3(-500, y, 1500), new Vec3(700, y, 1500));
            return new MepElementData(template.UniqueId, id, template.Category, template.Segments);
        }

        private static StructuralElementData Wall(long id, double x)
        {
            StructuralElementData wall = TestData.Wall("wall-" + id, x);
            return new StructuralElementData(wall.UniqueId, id, wall.Category, wall.Mesh) { FaceNormal = wall.FaceNormal };
        }

        private static ClearanceSetup Setup() => new ClearanceSetup(
            new[] { new ClearanceRule(MepCategory.Conduit, 26, 50, 30, ClearanceMode.PerSide) }, 50, ClearanceMode.PerSide,
            new OpeningSettings());

        private static AnalysisRequest Request(ClearanceSetup setup = null) => new AnalysisRequest(
            TestData.StrRef, new[] { StructuralCategory.Wall }, TestData.MepRef, new[] { MepCategory.Conduit }, setup ?? Setup());

        private static AnalyzeClashesUseCase UseCase(FakeGeometry geometry) => new AnalyzeClashesUseCase(geometry, new InlineDispatcher());

        [Fact]
        public async Task A_conduit_through_a_wall_gives_one_opening_sized_by_the_rules()
        {
            var geometry = new FakeGeometry { Mep = { Conduit(1, 500) }, Structural = { Wall(10, 0) } };

            AnalysisResult result = await UseCase(geometry).RunAsync(Request());

            OpeningData opening = Assert.Single(result.Openings);
            Assert.Equal(OpeningShape.Circular, opening.Shape);
            Assert.Equal(100, opening.DiameterMm, 3);   // 40 + 2 × 30
            Assert.Equal(300, opening.DepthMm, 3);      // 200 wall + 2 × 50 overshoot
            Assert.Equal(OpeningStatus.Detected, opening.Status);
            Assert.Equal(1, result.MepElementCount);
            Assert.Equal(1, result.StructuralElementCount);
            Assert.Empty(result.NotAnalysed);
        }

        [Fact]
        public async Task Only_ticked_categories_are_read()
        {
            var geometry = new FakeGeometry
            {
                Mep = { Conduit(1, 500), TestData.Round("pipe", MepCategory.Pipe, 100, new Vec3(-500, 800, 1500), new Vec3(700, 800, 1500)) },
                Structural = { Wall(10, 0) },
            };

            AnalysisResult result = await UseCase(geometry).RunAsync(Request());

            Assert.Equal(1, result.MepElementCount);
            Assert.Single(result.Openings);
        }

        [Fact]
        public async Task Structural_elements_are_asked_for_only_near_the_mep_elements()
        {
            var geometry = new FakeGeometry { Mep = { Conduit(1, 500) }, Structural = { Wall(10, 0), Wall(11, 50000) } };

            AnalysisResult result = await UseCase(geometry).RunAsync(Request());

            Assert.True(geometry.RegionAsked.HasValue);
            Assert.Equal(1, result.StructuralElementCount);
            // Centreline x -500..700, grown by the 20 mm radius and the 100 mm margin.
            Assert.Equal(-620, geometry.RegionAsked.Value.Min.X, 3);
            Assert.Equal(820, geometry.RegionAsked.Value.Max.X, 3);
        }

        [Fact]
        public void The_region_grows_by_insulation_and_by_the_diagonal_of_a_rectangular_section()
        {
            var duct = new MepElementData("duct", 1, MepCategory.Duct, new[]
            {
                new MepSegment(new Segment3(new Vec3(0, 0, 0), new Vec3(1000, 0, 0)), MepProfile.Rectangular(300, 400)),
            }) { InsulationMm = 25 };

            Aabb region = AnalyzeClashesUseCase.RegionOf(new[] { duct });

            Assert.Equal(-(250 + 25 + 100), region.Min.Y, 3);
            Assert.Equal(1000 + 250 + 25 + 100, region.Max.X, 3);
        }

        [Fact]
        public async Task Geometry_is_read_in_batches()
        {
            var geometry = new FakeGeometry();
            for (int i = 0; i < AnalyzeClashesUseCase.MepBatchSize + 5; i++)
                geometry.Mep.Add(Conduit(i + 1, 10 + i % 900));
            for (int i = 0; i < AnalyzeClashesUseCase.StructuralBatchSize * 2 + 1; i++)
                geometry.Structural.Add(Wall(10000 + i, 0));

            await UseCase(geometry).RunAsync(Request());

            Assert.Equal(new[] { AnalyzeClashesUseCase.MepBatchSize, 5 }, geometry.MepBatches);
            Assert.Equal(new[] { AnalyzeClashesUseCase.StructuralBatchSize, AnalyzeClashesUseCase.StructuralBatchSize, 1 }, geometry.StructuralBatches);
        }

        [Fact]
        public async Task Elements_that_could_not_be_read_are_reported_not_dropped()
        {
            var geometry = new FakeGeometry
            {
                Mep = { Conduit(1, 500) },
                Structural = { Wall(10, 0) },
                SkippedMep = { new SkippedElement("MEP", 77, "Conduit Fittings", "No connectors.") },
            };

            AnalysisResult result = await UseCase(geometry).RunAsync(Request());

            SkippedElement skipped = Assert.Single(result.NotAnalysed);
            Assert.Equal(77, skipped.ElementId);
            Assert.Equal("No connectors.", skipped.Reason);
        }

        [Fact]
        public async Task No_mep_elements_gives_an_empty_result_without_reading_structure()
        {
            var geometry = new FakeGeometry { Structural = { Wall(10, 0) } };

            AnalysisResult result = await UseCase(geometry).RunAsync(Request());

            Assert.Empty(result.Openings);
            Assert.Null(geometry.RegionAsked);
            Assert.Empty(geometry.StructuralBatches);
        }

        [Fact]
        public async Task No_structure_near_the_mep_gives_an_empty_result()
        {
            var geometry = new FakeGeometry { Mep = { Conduit(1, 500) }, Structural = { Wall(11, 50000) } };

            AnalysisResult result = await UseCase(geometry).RunAsync(Request());

            Assert.Empty(result.Openings);
            Assert.Equal(1, result.MepElementCount);
            Assert.Equal(0, result.StructuralElementCount);
        }

        [Fact]
        public async Task Progress_goes_from_zero_to_one_and_never_backwards()
        {
            var geometry = new FakeGeometry { Mep = { Conduit(1, 300), Conduit(2, 600) }, Structural = { Wall(10, 0) } };
            var progress = new Collect();

            await UseCase(geometry).RunAsync(Request(), progress);

            Assert.Equal(0, progress.Reports.First().Fraction);
            Assert.Equal(1, progress.Reports.Last().Fraction);
            for (int i = 1; i < progress.Reports.Count; i++)
                Assert.True(progress.Reports[i].Fraction >= progress.Reports[i - 1].Fraction);
        }

        [Fact]
        public async Task Cancelling_between_batches_stops_the_run()
        {
            var cancel = new CancellationTokenSource();
            var geometry = new FakeGeometry { Mep = { Conduit(1, 500) } };
            for (int i = 0; i < AnalyzeClashesUseCase.StructuralBatchSize * 3; i++)
                geometry.Structural.Add(Wall(10000 + i, 0));
            geometry.OnStructuralBatch = () => cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UseCase(geometry).RunAsync(Request(), null, cancel.Token));

            Assert.Single(geometry.StructuralBatches);
        }

        [Theory]
        [InlineData(false, true, true, true, "structural link is not loaded")]
        [InlineData(true, false, true, true, "MEP link is not loaded")]
        [InlineData(true, true, false, true, "structural category")]
        [InlineData(true, true, true, false, "MEP category")]
        public async Task A_request_that_cannot_run_says_why(bool structuralLoaded, bool mepLoaded, bool structuralTicked, bool mepTicked, string expected)
        {
            var request = new AnalysisRequest(
                new ModelRef("s", "S") { IsLoaded = structuralLoaded },
                structuralTicked ? new[] { StructuralCategory.Wall } : new StructuralCategory[0],
                new ModelRef("m", "M") { IsLoaded = mepLoaded },
                mepTicked ? new[] { MepCategory.Pipe } : new MepCategory[0],
                Setup());

            Assert.Contains(expected, request.Problem());
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => UseCase(new FakeGeometry()).RunAsync(request));
            Assert.Equal(request.Problem(), error.Message);
        }

        [Fact]
        public void A_request_without_a_valid_clearance_setup_points_at_the_card()
        {
            var request = new AnalysisRequest(TestData.StrRef, new[] { StructuralCategory.Wall }, TestData.MepRef, new[] { MepCategory.Pipe }, null);

            Assert.Contains("OPENING CLEARANCE", request.Problem());
        }

        [Fact]
        public async Task A_model_that_is_no_longer_available_fails_with_its_message()
        {
            var useCase = new AnalyzeClashesUseCase(new ThrowingGeometry(), new InlineDispatcher());

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.RunAsync(Request()));

            Assert.Equal("The MEP link is no longer loaded.", error.Message);
        }

        private sealed class ThrowingGeometry : IGeometryReader
        {
            public IReadOnlyList<long> ListMep(ModelRef model, IReadOnlyList<MepCategory> categories)
                => throw new InvalidOperationException("The MEP link is no longer loaded.");
            public GeometryBatch<MepElementData> ReadMep(ModelRef model, IReadOnlyList<long> ids) => throw new NotSupportedException();
            public IReadOnlyList<long> ListStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories, Aabb? regionMm) => throw new NotSupportedException();
            public GeometryBatch<StructuralElementData> ReadStructural(ModelRef model, IReadOnlyList<long> ids) => throw new NotSupportedException();
        }

        // ---- review decisions ----

        private static OpeningData Opening(string mepId, OpeningStatus status = OpeningStatus.Detected) => new OpeningData
        {
            Clash = new ClashData
            {
                Key = new ClashKey("s", "wall", "m", mepId, 0),
                StructuralModel = TestData.StrRef, MepModel = TestData.MepRef,
                Structural = TestData.Wall("wall"), Mep = Conduit(5, 500),
            },
            Status = status,
            Shape = OpeningShape.Circular, DiameterMm = 100, DepthMm = 300,
            Orientation = new Frame3(new Vec3(100, 500, 1500), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX),
        };

        [Fact]
        public void A_plain_opening_starts_approved_and_one_for_review_does_not()
        {
            Assert.True(new ReviewedOpening(Opening("a")).IsApproved);

            var review = new ReviewedOpening(Opening("b", OpeningStatus.Review));
            Assert.False(review.IsApproved);
            Assert.Equal(OpeningStatus.Review, review.Status);
        }

        [Fact]
        public void Reject_then_approve_brings_the_opening_back_with_its_detected_status()
        {
            var row = new ReviewedOpening(Opening("b", OpeningStatus.Review));

            row.Reject();
            Assert.True(row.IsRejected);
            Assert.False(row.IsApproved);

            row.Unapprove();
            Assert.True(row.IsRejected);

            row.Approve();
            Assert.Equal(OpeningStatus.Review, row.Status);
            Assert.True(row.IsApproved);
        }

        [Fact]
        public void A_new_analysis_keeps_the_decisions_made_on_the_same_clashes()
        {
            var rejected = new ReviewedOpening(Opening("a")); rejected.Reject();
            var unticked = new ReviewedOpening(Opening("b")); unticked.Unapprove();
            var approvedReview = new ReviewedOpening(Opening("c", OpeningStatus.Review)); approvedReview.Approve();

            IReadOnlyList<ReviewedOpening> rows = ReviewedOpening.From(
                new[] { Opening("a"), Opening("b"), Opening("c", OpeningStatus.Review), Opening("d") },
                new[] { rejected, unticked, approvedReview });

            Assert.True(rows[0].IsRejected);
            Assert.False(rows[1].IsApproved);
            Assert.False(rows[1].IsRejected);
            Assert.True(rows[2].IsApproved);
            Assert.True(rows[3].IsApproved);
        }

        [Fact]
        public void The_zoom_box_is_centred_on_the_opening_with_a_margin()
        {
            Aabb box = ReviewedOpening.ZoomBox(Opening("a"), 1000);

            // Depth 300 is the largest dimension: half of it plus the margin on every side.
            Assert.Equal(100 - 1150, box.Min.X, 3);
            Assert.Equal(1500 + 1150, box.Max.Z, 3);
        }

        [Fact]
        public async Task Zoom_and_highlight_ask_the_navigator_for_the_right_things()
        {
            var navigator = new FakeNavigator();
            var service = new NavigationService(navigator, new InlineDispatcher());
            OpeningData opening = Opening("a");

            await service.ZoomToAsync(opening);
            await service.HighlightMepAsync(opening);
            await service.HighlightStructureAsync(opening);

            Assert.True(navigator.Zoomed.HasValue);
            Assert.Equal(new[] { ("MEP", 5L), ("STR", 100L) }, navigator.Selected.ToArray());
        }

        private sealed class FakeNavigator : IViewNavigator
        {
            public Aabb? Zoomed;
            public List<(string, long)> Selected = new List<(string, long)>();

            public void ZoomTo(Aabb boxMm) => Zoomed = boxMm;

            public void Select(ModelRef model, IReadOnlyList<long> elementIds) => Selected.Add((model.DisplayName, elementIds.Single()));
        }
    }
}
