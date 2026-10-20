using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using System.Text.Json;
using Xunit;

namespace RoofPointsAndSlope.Tests
{
    public class SettingsTests
    {
        // settings.json as an earlier version wrote it: no edge scopes, no SaveRunReport, bad counts.
        private const string OldFile = @"{
  ""SchemaVersion"": 1,
  ""InnerPointsEnabled"": false,
  ""CircularPoints"": 0,
  ""PathSamples"": 1,
  ""MinPointSpacingMm"": -5,
  ""SlopePercent"": 2.0
}";

        [Fact]
        public void Old_settings_file_loads_with_defaults_for_new_options()
        {
            var s = JsonSerializer.Deserialize<PipelineSettings>(OldFile).Normalize();
            Assert.False(s.InnerPointsEnabled);
            Assert.Equal(2.0, s.SlopePercent);
            Assert.False(s.SaveRunReport); // new option keeps the old behaviour
            Assert.NotNull(s.MidQuarterScope);
            Assert.NotNull(s.GapScope);
            Assert.Equal(1, s.CircularPoints);
            Assert.Equal(2, s.PathSamples);
            Assert.Equal(0, s.MinPointSpacingMm);
            Assert.Equal(PipelineSettings.CurrentSchemaVersion, s.SchemaVersion);
        }

        [Fact]
        public void Null_sections_are_filled_in()
        {
            var s = JsonSerializer.Deserialize<PipelineSettings>(
                @"{ ""MidQuarterScope"": null, ""GapScope"": null, ""ExportFolder"": null, ""RidgeLineStyle"": null }").Normalize();
            Assert.NotNull(s.MidQuarterScope);
            Assert.NotNull(s.GapScope);
            Assert.Equal(string.Empty, s.ExportFolder);
            Assert.Equal(string.Empty, s.RidgeLineStyle);
        }

        [Fact]
        public void Clone_does_not_share_edge_scopes()
        {
            var s = new PipelineSettings();
            var copy = s.Clone();
            copy.GapScope.Openings = true;
            copy.MidQuarterScope.Curves = false;
            Assert.False(s.GapScope.Openings);
            Assert.True(s.MidQuarterScope.Curves);
        }
    }
}
