using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;
using static VoidOpenings.Tests.TestData;

namespace VoidOpenings.Tests
{
    public class TrackingTests
    {
        private static readonly StructuralCategory[] AllStructural = { StructuralCategory.Wall, StructuralCategory.Floor };
        private static readonly MepCategory[] AllMep = { MepCategory.Pipe, MepCategory.Conduit };

        private static OpeningData PipeOpening(double diameter = 100, double y = 500)
        {
            var settings = VoidOpeningsSettings.CreateDefault();
            var pipe = Round("P1", MepCategory.Pipe, diameter, new Vec3(-500, y, 1500), new Vec3(700, y, 1500));
            ClashData clash = new ClashEngine().Detect(Structure(Wall("W1")), Mep(pipe)).Single();
            return new OpeningCalculator(settings.ToRuleSet(), settings.ToOpeningSettings()).Calculate(clash);
        }

        private static StoredOpening StoredFor(OpeningData opening, string status = "Cut")
        {
            string signature = OpeningSignature.Of(opening, Transform3.Identity).ToString();
            return new StoredOpening
            {
                ClashKey = opening.Clash.Key.ToString(), ClashId = opening.ClashId, JobId = "VOID-2026-000004",
                Signature = signature, CurrentSignature = signature, InstanceUniqueId = "inst-1", Status = status,
                MepModelKey = MepRef.ModelKey, StructuralCategory = "Wall", MepCategory = "Pipe",
            };
        }

        private static List<StoredOpening> Reconcile(OpeningData opening, params StoredOpening[] stored)
            => OpeningReconciler.Reconcile(opening == null ? new OpeningData[0] : new[] { opening }, stored,
                Transform3.Identity, MepRef.ModelKey, AllStructural, AllMep).ToList();

        [Fact]
        public void Transform_inverse_undoes_the_transform()
        {
            // Link rotated 90 degrees about Z and moved.
            var t = new Transform3(new Vec3(1000, 2000, 300), Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ);
            var p = new Vec3(10, 20, 30);

            AssertNear(new Vec3(980, 2010, 330), t.Apply(p));
            AssertNear(p, t.Inverse().Apply(t.Apply(p)));
        }

        [Fact]
        public void Signature_survives_a_text_round_trip_and_matches_itself()
        {
            OpeningSignature signature = OpeningSignature.Of(PipeOpening(), Transform3.Identity);

            Assert.True(OpeningSignature.TryParse(signature.ToString(), out OpeningSignature parsed));
            Assert.True(signature.Matches(parsed));
        }

        [Fact]
        public void Signature_does_not_match_an_opening_that_moved_or_changed_size()
        {
            OpeningSignature original = OpeningSignature.Of(PipeOpening(), Transform3.Identity);

            Assert.False(original.Matches(OpeningSignature.Of(PipeOpening(y: 505), Transform3.Identity)));
            Assert.False(original.Matches(OpeningSignature.Of(PipeOpening(diameter: 80), Transform3.Identity)));
        }

        [Fact]
        public void Signature_is_expressed_in_the_structural_models_own_coordinates()
        {
            var linkToHost = new Transform3(new Vec3(1000, 0, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

            OpeningSignature signature = OpeningSignature.Of(PipeOpening(), linkToHost.Inverse());

            AssertNear(new Vec3(-900, 500, 1500), signature.Center);
        }

        [Fact]
        public void Opening_with_no_stored_record_is_new()
        {
            OpeningData opening = PipeOpening();

            var orphans = Reconcile(opening);

            Assert.Equal(TrackingState.New, opening.Tracking);
            Assert.Null(opening.JobId);
            Assert.Empty(orphans);
        }

        [Fact]
        public void Opening_that_matches_its_record_is_existing_and_keeps_job_id_and_status()
        {
            OpeningData opening = PipeOpening();

            Reconcile(opening, StoredFor(opening));

            Assert.Equal(TrackingState.Existing, opening.Tracking);
            Assert.Equal("VOID-2026-000004", opening.JobId);
            Assert.Equal(OpeningStatus.Cut, opening.Status);
            Assert.Equal("inst-1", opening.InstanceUniqueId);
        }

        [Fact]
        public void Opening_whose_source_changed_is_updated_and_keeps_its_job_id()
        {
            StoredOpening record = StoredFor(PipeOpening(diameter: 80));
            OpeningData opening = PipeOpening(diameter: 100);

            Reconcile(opening, record);

            Assert.Equal(TrackingState.Updated, opening.Tracking);
            Assert.Equal("VOID-2026-000004", opening.JobId);
            Assert.Equal(OpeningStatus.Detected, opening.Status);
        }

        [Fact]
        public void Void_instance_changed_by_hand_is_reported_as_manually_modified()
        {
            OpeningData opening = PipeOpening();
            StoredOpening record = StoredFor(opening);
            record.CurrentSignature = OpeningSignature.Of(PipeOpening(y: 600), Transform3.Identity).ToString();

            Reconcile(opening, record);

            Assert.Equal(TrackingState.ManuallyModified, opening.Tracking);
        }

        [Fact]
        public void Record_whose_clash_is_gone_is_returned_as_source_deleted()
        {
            StoredOpening record = StoredFor(PipeOpening());

            var orphans = Reconcile(null, record);

            Assert.Same(record, Assert.Single(orphans));
        }

        [Fact]
        public void Record_of_another_mep_model_or_an_unselected_category_is_not_an_orphan()
        {
            StoredOpening otherModel = StoredFor(PipeOpening());
            otherModel.MepModelKey = "file:other.rvt";
            StoredOpening otherCategory = StoredFor(PipeOpening());
            otherCategory.MepCategory = "Duct";

            Assert.Empty(Reconcile(null, otherModel, otherCategory));
        }

        [Fact]
        public void Default_settings_follow_the_brief_and_are_valid()
        {
            VoidOpeningsSettings settings = VoidOpeningsSettings.CreateDefault();

            Assert.Empty(RuleValidator.Validate(settings));
            Assert.Equal(40, settings.ToRuleSet().Resolve(MepCategory.Conduit, 40).ClearanceMm);
            Assert.Equal(75, settings.ToRuleSet().Resolve(MepCategory.Duct, 400).ClearanceMm);
        }

        [Fact]
        public void Validator_reports_overlapping_ranges_reversed_ranges_and_negative_offsets()
        {
            var settings = new VoidOpeningsSettings();
            settings.Rules.Add(new ClearanceRuleDto { Category = "Pipe", FromMm = 0, ToMm = 50, ClearanceMm = 25 });
            settings.Rules.Add(new ClearanceRuleDto { Category = "Pipe", FromMm = 40, ToMm = 100, ClearanceMm = 40 });
            settings.Rules.Add(new ClearanceRuleDto { Category = "Duct", FromMm = 500, ToMm = 100, ClearanceMm = 75 });
            settings.Rules.Add(new ClearanceRuleDto { Category = "Conduit", FromMm = 0, ToMm = 25, ClearanceMm = -5 });

            IReadOnlyList<string> problems = RuleValidator.Validate(settings);

            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, p => p.Contains("overlap"));
        }

        [Fact]
        public void Settings_survive_a_json_round_trip_and_old_files_get_defaults()
        {
            VoidOpeningsSettings settings = VoidOpeningsSettings.CreateDefault();
            settings.RoundUpToMm = 25;

            VoidOpeningsSettings loaded = SettingsSerializer.FromJson(SettingsSerializer.ToJson(settings));
            VoidOpeningsSettings fromOld = SettingsSerializer.FromJson("{\"Rules\":[]}");

            Assert.Equal(25, loaded.RoundUpToMm);
            Assert.Equal(settings.Rules.Count, loaded.Rules.Count);
            Assert.Equal(50, fromOld.OvershootMm);
            Assert.Equal(VoidOpeningsSettings.CurrentVersion, fromOld.Version);
        }

        [Fact]
        public void Package_carries_openings_in_structural_coordinates_and_round_trips()
        {
            OpeningData opening = PipeOpening();
            var linkToHost = new Transform3(new Vec3(1000, 0, 0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

            OpeningPackage package = OpeningPackageBuilder.Build(StrRef, MepRef, new[] { opening }, linkToHost);
            OpeningPackage loaded = PackageSerializer.FromJson(PackageSerializer.ToJson(package));
            VoidPlacement placement = Assert.Single(loaded.Openings);

            Assert.Equal(StrRef.ModelKey, loaded.StructuralModelKey);
            Assert.Equal(-900, placement.CenterX, 2);
            Assert.Equal(opening.ClashId, placement.ClashId);
            Assert.Equal("W1", placement.StructuralUniqueId);
            Assert.Equal(180, placement.DiameterMm, 2);
            Assert.Equal("Circular", placement.Shape);
        }

        [Fact]
        public void Export_rows_keep_the_column_order_of_the_results_table()
        {
            Assert.Equal(new[]
            {
                "Clash ID", "Job ID", "Structural Model", "Structural Category", "Structural Element", "MEP Model",
                "MEP Category", "MEP Element", "Opening Width", "Opening Height", "Diameter", "Clearance", "Status",
            }, ExportRow.Headers.Take(13).ToArray());
        }
    }
}
