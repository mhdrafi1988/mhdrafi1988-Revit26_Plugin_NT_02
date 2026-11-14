using System;
using System.IO;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers;
using Xunit;

namespace AutoSlopeByPoint.Tests
{
    public class SettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "AutoSlopeV033Tests_" + Guid.NewGuid().ToString("N"));

        public SettingsTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* temp folder */ }
        }

        private string Write(string name, string json)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void V032File_Loads_WithRidgeLinesOff()
        {
            string v032 = Write("settings.v032.json",
                "{ \"SlopePercent\": 2.0, \"MaxDrainDistanceM\": 60, \"MarkersExpanded\": true," +
                "  \"HighestPointMarkerGroup\": { \"IsEnabled\": true, \"ColorName\": \"Red\", \"ShowCornerOffsetText\": true } }");

            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(Path.Combine(_dir, "settings.v033.json"), v032);

            Assert.Equal(2.0, s.SlopePercent);
            Assert.Equal(60, s.MaxDrainDistanceM);
            Assert.True(s.MarkersExpanded);
            Assert.True(s.HighestPointMarkerGroup.ShowCornerOffsetText);
            Assert.False(s.DrawRidgeLines);
            Assert.Null(s.RidgeLineStyleName);
            Assert.False(s.RidgeLinesExpanded);
        }

        [Fact]
        public void V033File_WinsOverV032File()
        {
            string v033 = Write("settings.v033.json",
                "{ \"SlopePercent\": 1.0, \"DrawRidgeLines\": true, \"RidgeLineStyleName\": \"Medium Lines\" }");
            string v032 = Write("settings.v032.json", "{ \"SlopePercent\": 2.5 }");

            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(v033, v032);

            Assert.Equal(1.0, s.SlopePercent);
            Assert.True(s.DrawRidgeLines);
            Assert.Equal("Medium Lines", s.RidgeLineStyleName);
        }

        [Fact]
        public void BrokenFile_GivesDefaults()
        {
            string v033 = Write("settings.v033.json", "{ not json");
            string v032 = Write("settings.v032.json", "{ \"SlopePercent\": 2.5 }");

            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(v033, v032);

            Assert.Equal(AutoSlopeConstants.DefaultSlopePercent, s.SlopePercent);
            Assert.False(s.DrawRidgeLines);
        }

        [Fact]
        public void NoFile_GivesDefaults()
        {
            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(Path.Combine(_dir, "missing.json"));

            Assert.Equal(AutoSlopeConstants.DefaultSlopePercent, s.SlopePercent);
            Assert.False(s.DrawRidgeLines);
        }
    }
}
