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
    /// The seam between the window and the void writer: the clearance setup of the card turned into
    /// the settings of a run, and the opening package written in a linked model's own coordinates.
    /// </summary>
    public class PlacementServiceTests
    {
        /// <summary>Runs the work on the calling thread, standing in for the Revit dispatcher.</summary>
        private sealed class Inline : IRevitDispatcher
        {
            public Task<T> RunAsync<T>(Func<T> work) => Task.FromResult(work());
        }

        /// <summary>Answers only <see cref="IModelReader.GetToHostTransform"/>; nothing else is called here.</summary>
        private sealed class FakeModels : IModelReader
        {
            public Transform3 ToHost = Transform3.Identity;

            public Transform3 GetToHostTransform(ModelRef model) => ToHost;

            public IReadOnlyList<ModelRef> GetModels() => throw new NotSupportedException();
            public IReadOnlyDictionary<StructuralCategory, int> CountStructural(ModelRef model) => throw new NotSupportedException();
            public IReadOnlyDictionary<MepCategory, int> CountMep(ModelRef model) => throw new NotSupportedException();
        }

        /// <summary>Accepts every placement and reports it as cut.</summary>
        private sealed class FakeWriter : IVoidWriter
        {
            public VoidOpeningsSettings Settings;

            public IReadOnlyList<PlacementOutcome> Place(IReadOnlyList<VoidPlacement> placements, VoidOpeningsSettings settings, ICollection<string> notes)
            {
                Settings = settings;
                return placements.Select((p, i) => new PlacementOutcome
                {
                    ClashKey = p.ClashKey, JobId = JobIdGenerator.Format(2026, i + 1),
                    InstanceUniqueId = "inst-" + i, Status = "Cut", Message = string.Empty,
                }).ToList();
            }
        }

        private static OpeningData PipeOpening()
        {
            VoidOpeningsSettings settings = VoidOpeningsSettings.CreateDefault();
            var pipe = Round("P1", MepCategory.Pipe, 100, new Vec3(-500, 500, 1500), new Vec3(700, 500, 1500));
            ClashData clash = new ClashEngine().Detect(Structure(Wall("W1")), Mep(pipe)).Single();
            return new OpeningCalculator(settings.ToRuleSet(), settings.ToOpeningSettings()).Calculate(clash);
        }

        private static VoidPlacementService Service(IVoidWriter writer, IModelReader models)
            => new VoidPlacementService(new GenerateVoidsUseCase(writer, new Inline()), new Inline(), models);

        [Fact]
        public void The_clearance_setup_of_the_card_becomes_the_settings_of_a_run()
        {
            var setup = new ClearanceSetup(
                new[] { new ClearanceRule(MepCategory.Duct, 0, 400, 65, ClearanceMode.Total, OpeningShapePolicy.Rectangular) },
                30, ClearanceMode.Total,
                new OpeningSettings
                {
                    OvershootMm = 20, RoundUpToMm = 25, IncludeInsulation = false,
                    AxisMode = OpeningAxisMode.FollowMep, MaxCircularAngleDeg = 30,
                });

            VoidOpeningsSettings settings = VoidOpeningsSettings.From(setup);

            Assert.Equal(30, settings.DefaultClearanceMm);
            Assert.Equal(nameof(ClearanceMode.Total), settings.DefaultMode);
            Assert.Equal(20, settings.OvershootMm);
            Assert.Equal(25, settings.RoundUpToMm);
            Assert.False(settings.IncludeInsulation);
            Assert.Equal(nameof(OpeningAxisMode.FollowMep), settings.AxisMode);
            Assert.Equal(30, settings.MaxCircularAngleDeg);
            Assert.Equal(string.Empty, settings.CustomRoundFamily);

            ClearanceRuleDto rule = Assert.Single(settings.Rules);
            Assert.Equal(nameof(MepCategory.Duct), rule.Category);
            Assert.Equal(400, rule.ToMm);
            Assert.Equal(65, rule.ClearanceMm);
            Assert.Equal(nameof(OpeningShapePolicy.Rectangular), rule.Shape);

            // The same setup, read back by the run: the rule and the options survive the trip.
            Assert.Equal(65, settings.ToRuleSet().Rules.Single().ClearanceMm);
            Assert.Equal(OpeningAxisMode.FollowMep, settings.ToOpeningSettings().AxisMode);
        }

        [Fact]
        public void A_rule_with_no_upper_size_limit_keeps_covering_every_size()
        {
            var setup = new ClearanceSetup(
                new[] { new ClearanceRule(MepCategory.Pipe, 100, ClearanceRule.NoUpperLimit, 80, ClearanceMode.PerSide) },
                50, ClearanceMode.PerSide, new OpeningSettings());

            ClearanceRule rule = VoidOpeningsSettings.From(setup).ToRuleSet().Rules.Single();

            Assert.False(rule.HasUpperLimit);
            Assert.True(rule.Covers(5000));
        }

        [Fact]
        public async Task Generate_hands_the_card_settings_to_the_writer()
        {
            var writer = new FakeWriter();
            var setup = new ClearanceSetup(null, 42, ClearanceMode.Total, new OpeningSettings { OvershootMm = 15 });

            await Service(writer, new FakeModels()).GenerateAsync(new[] { PipeOpening() }, setup);

            Assert.Equal(42, writer.Settings.DefaultClearanceMm);
            Assert.Equal(15, writer.Settings.OvershootMm);
        }

        [Fact]
        public async Task A_package_holds_the_openings_in_the_linked_structural_models_own_coordinates()
        {
            OpeningData opening = PipeOpening();
            // The structural link sits 1000 mm along X from the host origin.
            var models = new FakeModels
            {
                ToHost = new Transform3(new Vec3(1000, 0, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ),
            };

            OpeningPackage package = await Service(new FakeWriter(), models)
                .BuildPackageAsync(StrRef, MepRef, new[] { opening });

            Assert.Equal(StrRef.ModelKey, package.StructuralModelKey);
            Assert.Equal(MepRef.DisplayName, package.MepModelName);
            VoidPlacement placement = Assert.Single(package.Openings);
            Assert.Equal(opening.Center.X - 1000, placement.CenterX, 3);
            Assert.Equal(opening.Center.Y, placement.CenterY, 3);
            Assert.Equal(opening.Center.Z, placement.CenterZ, 3);
            Assert.Equal("W1", placement.StructuralUniqueId);
        }

        [Fact]
        public async Task Applying_a_package_places_its_openings_and_counts_them()
        {
            OpeningData opening = PipeOpening();
            var models = new FakeModels();
            VoidPlacementService service = Service(new FakeWriter(), models);
            OpeningPackage package = await service.BuildPackageAsync(StrRef, MepRef, new[] { opening });

            GenerateSummary summary = await service.ApplyPackageAsync(package, ClearanceSetup.CreateDefault());

            Assert.Equal(1, summary.Cut);
            Assert.Equal(0, summary.Failed);
        }
    }
}
