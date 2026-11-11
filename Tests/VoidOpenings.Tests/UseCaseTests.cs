using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClashDetection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Placement;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;
using static VoidOpenings.Tests.TestData;

namespace VoidOpenings.Tests
{
    public class UseCaseTests
    {
        private sealed class Inline : IRevitDispatcher
        {
            public Task<T> RunAsync<T>(Func<T> work) => Task.FromResult(work());
        }

        private sealed class FakeReader : IModelReader
        {
            public Aabb? BoundsAsked;
            public List<StoredOpening> Stored = new List<StoredOpening>();

            public IReadOnlyList<ModelRef> GetModels() => new[] { StrRef, MepRef };
            public IReadOnlyDictionary<StructuralCategory, int> CountStructural(ModelRef model) => new Dictionary<StructuralCategory, int>();
            public IReadOnlyDictionary<MepCategory, int> CountMep(ModelRef model) => new Dictionary<MepCategory, int>();

            public MepModel ReadMep(ModelRef model, IReadOnlyList<MepCategory> categories, ICollection<string> notes)
            {
                notes.Add("1 fitting could not be analysed.");
                return Mep(Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500)));
            }

            public StructuralModel ReadStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories, Aabb? hostBoundsMm, ICollection<string> notes)
            {
                BoundsAsked = hostBoundsMm;
                return Structure(Wall("W1"));
            }

            public IReadOnlyList<StoredOpening> ReadStoredOpenings(ModelRef structuralModel) => Stored;
        }

        private sealed class FakeWriter : IVoidWriter
        {
            public IReadOnlyList<VoidPlacement> Received;
            public string Status = "Cut";

            public IReadOnlyList<PlacementOutcome> Place(IReadOnlyList<VoidPlacement> placements, VoidOpeningsSettings settings, ICollection<string> notes)
            {
                Received = placements;
                return placements.Select((p, i) => new PlacementOutcome
                {
                    ClashKey = p.ClashKey, JobId = Status == "Failed" ? null : JobIdGenerator.Format(2026, i + 1),
                    InstanceUniqueId = Status == "Failed" ? null : "inst-" + i, Status = Status,
                    Message = Status == "Failed" ? "Element is owned by another user." : string.Empty,
                }).ToList();
            }
        }

        private static AnalysisRequest Request() => new AnalysisRequest
        {
            Structural = StrRef, StructuralCategories = new[] { StructuralCategory.Wall },
            Mep = MepRef, MepCategories = new[] { MepCategory.Pipe },
            Settings = VoidOpeningsSettings.CreateDefault(),
        };

        [Fact]
        public async Task Analyze_reads_both_models_and_returns_sized_openings_with_the_readers_notes()
        {
            var reader = new FakeReader();

            AnalysisResult result = await new AnalyzeClashesUseCase(reader, new Inline()).RunAsync(Request(), null, CancellationToken.None);

            OpeningData opening = Assert.Single(result.Openings);
            Assert.Equal(180, opening.DiameterMm, 3);
            Assert.Equal(TrackingState.New, opening.Tracking);
            Assert.Contains("1 fitting could not be analysed.", result.Notes);
            Assert.Equal(1, result.MepElementCount);
            Assert.Equal(1, result.StructuralElementCount);
        }

        [Fact]
        public async Task Analyze_asks_for_structure_only_near_the_mep_elements()
        {
            var reader = new FakeReader();

            await new AnalyzeClashesUseCase(reader, new Inline()).RunAsync(Request(), null, CancellationToken.None);

            Assert.True(reader.BoundsAsked.HasValue);
            Assert.True(reader.BoundsAsked.Value.Min.X < -500 && reader.BoundsAsked.Value.Max.X > 700);
        }

        [Fact]
        public async Task Analyze_marks_an_opening_that_already_has_a_void_as_existing()
        {
            var reader = new FakeReader();
            var useCase = new AnalyzeClashesUseCase(reader, new Inline());
            OpeningData first = (await useCase.RunAsync(Request(), null, CancellationToken.None)).Openings.Single();
            string signature = OpeningSignature.Of(first, Transform3.Identity).ToString();
            reader.Stored.Add(new StoredOpening
            {
                ClashKey = first.Clash.Key.ToString(), JobId = "VOID-2026-000009", Status = "Cut",
                Signature = signature, CurrentSignature = signature, MepModelKey = MepRef.ModelKey,
            });

            OpeningData again = (await useCase.RunAsync(Request(), null, CancellationToken.None)).Openings.Single();

            Assert.Equal(TrackingState.Existing, again.Tracking);
            Assert.Equal("VOID-2026-000009", again.JobId);
        }

        [Theory]
        [InlineData(true, false, "structural")]
        [InlineData(false, true, "MEP")]
        public async Task Analyze_refuses_to_start_without_categories(bool noStructural, bool noMep, string expectedWord)
        {
            AnalysisRequest request = Request();
            if (noStructural) request.StructuralCategories = new StructuralCategory[0];
            if (noMep) request.MepCategories = new MepCategory[0];

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AnalyzeClashesUseCase(new FakeReader(), new Inline()).RunAsync(request, null, CancellationToken.None));

            Assert.Contains(expectedWord, error.Message);
        }

        [Fact]
        public async Task Generate_sends_placements_and_writes_job_id_and_status_back_to_the_openings()
        {
            AnalysisResult analysis = await new AnalyzeClashesUseCase(new FakeReader(), new Inline()).RunAsync(Request(), null, CancellationToken.None);
            var writer = new FakeWriter();

            GenerateSummary summary = await new GenerateVoidsUseCase(writer, new Inline())
                .RunAsync(analysis.Openings, analysis.StructuralToHost, Request().Settings);

            OpeningData opening = analysis.Openings.Single();
            Assert.Equal("VOID-2026-000001", opening.JobId);
            Assert.Equal(OpeningStatus.Cut, opening.Status);
            Assert.Equal(TrackingState.Existing, opening.Tracking);
            Assert.Equal(1, summary.Cut);
            Assert.Equal("W1", writer.Received.Single().StructuralUniqueId);
        }

        [Fact]
        public async Task Generate_reports_a_failed_opening_with_its_reason_and_no_job_id()
        {
            AnalysisResult analysis = await new AnalyzeClashesUseCase(new FakeReader(), new Inline()).RunAsync(Request(), null, CancellationToken.None);

            GenerateSummary summary = await new GenerateVoidsUseCase(new FakeWriter { Status = "Failed" }, new Inline())
                .RunAsync(analysis.Openings, analysis.StructuralToHost, Request().Settings);

            OpeningData opening = analysis.Openings.Single();
            Assert.Equal(OpeningStatus.Failed, opening.Status);
            Assert.Null(opening.JobId);
            Assert.Contains("owned by another user", opening.ResultNote);
            Assert.Equal(1, summary.Failed);
        }
    }
}
