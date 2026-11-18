using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Json;
using Xunit;

namespace VoidOpenings.Tests
{
    public class SettingsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "VoidOpeningsTests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }

        private static ClearanceSetup Sample() => new ClearanceSetup(new[]
        {
            new ClearanceRule(MepCategory.Conduit, 0, 25, 25, ClearanceMode.PerSide, OpeningShapePolicy.Circular),
            new ClearanceRule(MepCategory.Conduit, 26, 50, 30, ClearanceMode.PerSide),
            new ClearanceRule(MepCategory.DuctFitting, 500.5, ClearanceRule.NoUpperLimit, 75, ClearanceMode.Total, OpeningShapePolicy.Rectangular),
        }, 35, ClearanceMode.Total, new OpeningSettings
        {
            OvershootMm = 60, RoundUpToMm = 10, IncludeInsulation = false,
            AxisMode = OpeningAxisMode.FollowMep, MaxCircularAngleDeg = 20,
        });

        private static void AssertSame(ClearanceSetup expected, ClearanceSetup actual)
        {
            Assert.Equal(expected.DefaultClearanceMm, actual.DefaultClearanceMm);
            Assert.Equal(expected.DefaultMode, actual.DefaultMode);
            Assert.Equal(expected.Options.OvershootMm, actual.Options.OvershootMm);
            Assert.Equal(expected.Options.RoundUpToMm, actual.Options.RoundUpToMm);
            Assert.Equal(expected.Options.IncludeInsulation, actual.Options.IncludeInsulation);
            Assert.Equal(expected.Options.AxisMode, actual.Options.AxisMode);
            Assert.Equal(expected.Options.MaxCircularAngleDeg, actual.Options.MaxCircularAngleDeg);
            Assert.Equal(expected.Rules.Count, actual.Rules.Count);
            for (int i = 0; i < expected.Rules.Count; i++)
            {
                Assert.Equal(expected.Rules[i].Category, actual.Rules[i].Category);
                Assert.Equal(expected.Rules[i].FromMm, actual.Rules[i].FromMm);
                Assert.Equal(expected.Rules[i].ToMm, actual.Rules[i].ToMm);
                Assert.Equal(expected.Rules[i].ClearanceMm, actual.Rules[i].ClearanceMm);
                Assert.Equal(expected.Rules[i].Mode, actual.Rules[i].Mode);
                Assert.Equal(expected.Rules[i].Shape, actual.Rules[i].Shape);
            }
        }

        [Fact]
        public void Json_round_trip_keeps_every_value()
        {
            ClearanceSetup read = SettingsJson.Read(SettingsJson.Write(Sample()), out string warning);

            Assert.Null(warning);
            AssertSame(Sample(), read);
        }

        [Fact]
        public void The_file_has_a_version_field_and_writes_enums_by_name_and_no_limit_as_null()
        {
            using JsonDocument document = JsonDocument.Parse(SettingsJson.Write(Sample()));
            JsonElement root = document.RootElement;

            Assert.Equal(SettingsJson.CurrentVersion, root.GetProperty("version").GetInt32());
            Assert.Equal("Total", root.GetProperty("defaultMode").GetString());
            JsonElement last = root.GetProperty("rules").EnumerateArray().Last();
            Assert.Equal("DuctFitting", last.GetProperty("category").GetString());
            Assert.Equal(JsonValueKind.Null, last.GetProperty("toMm").ValueKind);
        }

        [Fact]
        public void A_file_with_only_some_values_uses_the_defaults_for_the_rest()
        {
            ClearanceSetup read = SettingsJson.Read("{ \"defaultClearanceMm\": 20, \"rules\": [ { \"category\": \"Pipe\", \"clearanceMm\": 40 } ] }", out string warning);

            Assert.Null(warning);
            Assert.Equal(20, read.DefaultClearanceMm);
            Assert.Equal(ClearanceMode.PerSide, read.DefaultMode);
            Assert.Equal(50, read.Options.OvershootMm);
            Assert.True(read.Options.IncludeInsulation);
            ClearanceRule rule = Assert.Single(read.Rules);
            Assert.Equal(0, rule.FromMm);
            Assert.False(rule.HasUpperLimit);
            Assert.Equal(ClearanceMode.PerSide, rule.Mode);
            Assert.Equal(OpeningShapePolicy.Auto, rule.Shape);
        }

        [Fact]
        public void Rules_that_cannot_be_understood_are_skipped_with_a_warning_and_the_good_ones_kept()
        {
            const string json = @"{ ""version"": 1, ""rules"": [
                { ""category"": ""Pipe"", ""fromMm"": 0, ""toMm"": 50, ""clearanceMm"": 25 },
                { ""category"": ""Sprinkler"", ""clearanceMm"": 25 },
                { ""category"": ""Pipe"", ""clearanceMm"": -5 },
                { ""category"": ""Pipe"", ""fromMm"": 80, ""toMm"": 60, ""clearanceMm"": 25 },
                { ""category"": ""3"", ""clearanceMm"": 25 },
                { ""category"": ""Pipe"", ""clearanceMm"": 25, ""mode"": ""Sideways"" },
                ""not a rule"" ] }";

            ClearanceSetup read = SettingsJson.Read(json, out string warning);

            Assert.Single(read.Rules);
            Assert.Contains("6 rule(s)", warning);
        }

        [Fact]
        public void Wrong_option_values_fall_back_to_their_defaults_with_a_warning()
        {
            ClearanceSetup read = SettingsJson.Read(
                @"{ ""defaultClearanceMm"": ""wide"", ""overshootMm"": -1, ""includeInsulation"": ""yes"", ""axisMode"": ""Diagonal"", ""rules"": 5 }",
                out string warning);

            Assert.Equal(50, read.DefaultClearanceMm);
            Assert.Equal(50, read.Options.OvershootMm);
            Assert.True(read.Options.IncludeInsulation);
            Assert.Equal(OpeningAxisMode.PerpendicularToStructure, read.Options.AxisMode);
            Assert.Empty(read.Rules);
            Assert.Contains("defaultClearanceMm", warning);
            Assert.Contains("overshootMm", warning);
            Assert.Contains("includeInsulation", warning);
            Assert.Contains("axisMode", warning);
            Assert.Contains("rules", warning);
        }

        [Fact]
        public void A_file_from_a_newer_version_is_read_with_a_warning()
        {
            ClearanceSetup read = SettingsJson.Read(@"{ ""version"": 9, ""defaultClearanceMm"": 20, ""somethingNew"": true }", out string warning);

            Assert.Equal(20, read.DefaultClearanceMm);
            Assert.Contains("newer version", warning);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1, 2]")]
        [InlineData("{ \"version\": 1,")]
        public void Text_that_is_not_a_json_object_throws_JsonException(string text)
        {
            Assert.ThrowsAny<JsonException>(() => SettingsJson.Read(text, out _));
        }

        [Fact]
        public void A_store_with_no_file_gives_the_default_setup_and_no_warning()
        {
            SettingsLoadResult result = new FileSettingsStore(_folder).Load();

            Assert.Null(result.Warning);
            Assert.Empty(result.Setup.Rules);
            Assert.Equal(50, result.Setup.DefaultClearanceMm);
            Assert.False(Directory.Exists(_folder));
        }

        [Fact]
        public void Saved_rules_survive_a_restart()
        {
            new FileSettingsStore(_folder).Save(Sample());

            SettingsLoadResult result = new FileSettingsStore(_folder).Load();

            Assert.Null(result.Warning);
            AssertSame(Sample(), result.Setup);
            Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_folder).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void A_second_save_replaces_the_first()
        {
            var store = new FileSettingsStore(_folder);
            store.Save(Sample());
            store.Save(ClearanceSetup.CreateDefault());

            Assert.Empty(store.Load().Setup.Rules);
        }

        [Fact]
        public void A_damaged_file_gives_the_defaults_a_warning_and_a_copy_of_the_old_file()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ \"version\": 1, \"rules\": [");

            SettingsLoadResult result = new FileSettingsStore(_folder).Load();

            Assert.NotNull(result.Warning);
            Assert.Empty(result.Setup.Rules);
            string kept = Assert.Single(Directory.GetFiles(_folder, "settings.corrupt-*.json"));
            Assert.Equal("{ \"version\": 1, \"rules\": [", File.ReadAllText(kept));
        }

        [Fact]
        public void The_service_saves_a_valid_card_and_writes_nothing_for_a_card_with_errors()
        {
            var service = new ClearanceConfigurationService(new FileSettingsStore(_folder));
            var bad = new ClearanceInput { DefaultClearanceText = "50", OvershootText = "50" };
            bad.Rules.Add(new ClearanceRuleInput { Category = MepCategory.Pipe, FromText = "90", ToText = "10", ClearanceText = "25" });

            Assert.True(service.Save(bad).HasErrors);
            Assert.False(File.Exists(service.Location));

            var good = ClearanceInput.From(Sample());
            Assert.False(service.Save(good).HasErrors);
            AssertSame(Sample(), service.Load().Setup);
        }
    }
}
