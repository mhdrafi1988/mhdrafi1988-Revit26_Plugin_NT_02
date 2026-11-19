using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Placement;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;
using static VoidOpenings.Tests.TestData;

namespace VoidOpenings.Tests
{
    /// <summary>
    /// What <see cref="GenerateVoidsUseCase"/> writes back onto the openings it was given, with a fake
    /// void writer in place of Revit. Ported from the ccr-5831eed2-qc9usy branch: the writer double and
    /// the assertions are that branch's, while the openings are built here from the clash engine instead
    /// of by running Analyze, because this line's AnalysisResult carries no transform of its own.
    /// </summary>
    public class GenerateVoidsTests
    {
        /// <summary>Runs the work on the calling thread, standing in for the Revit dispatcher.</summary>
        private sealed class Inline : IRevitDispatcher
        {
            public Task<T> RunAsync<T>(Func<T> work) => Task.FromResult(work());
        }

        /// <summary>
        /// Accepts every placement and reports the same <see cref="Status"/> for each. With "Failed" it
        /// returns no job id and the message Revit gives for an element owned by someone else.
        /// </summary>
        private sealed class FakeWriter : IVoidWriter
        {
            public IReadOnlyList<VoidPlacement> Received;
            public string Status = "Cut";

            public IReadOnlyList<PlacementOutcome> Place(IReadOnlyList<VoidPlacement> placements, VoidOpeningsSettings settings, ICollection<string> notes)
            {
                Received = placements;
                return placements.Select((p, i) => new PlacementOutcome
                {
                    ClashKey = p.ClashKey,
                    JobId = Status == "Failed" ? null : JobIdGenerator.Format(2026, i + 1),
                    InstanceUniqueId = Status == "Failed" ? null : "inst-" + i,
                    Status = Status,
                    Message = Status == "Failed" ? "Element is owned by another user." : string.Empty,
                }).ToList();
            }
        }

        /// <summary>One opening for a 100 mm pipe through wall "W1", sized by the default clearance rules.</summary>
        private static OpeningData PipeOpening()
        {
            VoidOpeningsSettings settings = VoidOpeningsSettings.CreateDefault();
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));
            ClashData clash = new ClashEngine().Detect(Structure(Wall("W1")), Mep(pipe)).Single();
            return new OpeningCalculator(settings.ToRuleSet(), settings.ToOpeningSettings()).Calculate(clash);
        }

        [Fact]
        public async Task Generate_sends_placements_and_writes_job_id_and_status_back_to_the_openings()
        {
            OpeningData opening = PipeOpening();
            var writer = new FakeWriter();

            GenerateSummary summary = await new GenerateVoidsUseCase(writer, new Inline())
                .RunAsync(new[] { opening }, Transform3.Identity, VoidOpeningsSettings.CreateDefault());

            Assert.Equal("VOID-2026-000001", opening.JobId);
            Assert.Equal(OpeningStatus.Cut, opening.Status);
            Assert.Equal(TrackingState.Existing, opening.Tracking);
            Assert.Equal(1, summary.Cut);
            Assert.Equal("W1", writer.Received.Single().StructuralUniqueId);
        }

        [Fact]
        public async Task Generate_reports_a_failed_opening_with_its_reason_and_no_job_id()
        {
            OpeningData opening = PipeOpening();

            GenerateSummary summary = await new GenerateVoidsUseCase(new FakeWriter { Status = "Failed" }, new Inline())
                .RunAsync(new[] { opening }, Transform3.Identity, VoidOpeningsSettings.CreateDefault());

            Assert.Equal(OpeningStatus.Failed, opening.Status);
            Assert.Null(opening.JobId);
            Assert.Contains("owned by another user", opening.ResultNote);
            Assert.Equal(1, summary.Failed);
        }
    }
}
