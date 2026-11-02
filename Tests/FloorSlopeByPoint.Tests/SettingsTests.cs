using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Infrastructure.Helpers;
using Xunit;

namespace FloorSlopeByPoint.Tests
{
    public class SettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "FloorSlopeV001Tests_" + Guid.NewGuid().ToString("N"));

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
        public void SettingsPath_IsTheFloorToolsOwnFile()
        {
            Assert.EndsWith(Path.Combine("FloorSlopeByPoint", "settings.v001.json"), AutoSlopeSettingsStore.SettingsPath);
        }

        [Fact]
        public void V001File_Loads()
        {
            string v001 = Write("settings.v001.json",
                "{ \"SettingsVersion\": 1, \"SlopePercent\": 2.0, \"MaxDrainDistanceM\": 60, \"DrawRidgeLines\": true," +
                "  \"RidgeLineStyleName\": \"Medium Lines\" }");

            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(v001);

            Assert.Equal(1, s.SettingsVersion);
            Assert.Equal(2.0, s.SlopePercent);
            Assert.Equal(60, s.MaxDrainDistanceM);
            Assert.True(s.DrawRidgeLines);
            Assert.Equal("Medium Lines", s.RidgeLineStyleName);
        }

        [Fact]
        public void FileWithoutVersion_ReadsAsCurrentVersion_AndMissingFieldsAreDefaults()
        {
            string file = Write("settings.v001.json", "{ \"SlopePercent\": 1.25 }");

            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(file);

            Assert.Equal(AutoSlopeSettings.CurrentSettingsVersion, s.SettingsVersion);
            Assert.Equal(1.25, s.SlopePercent);
            Assert.False(s.DrawRidgeLines);
        }

        [Fact]
        public void NewSettings_SerializeTheVersionField()
        {
            string json = JsonSerializer.Serialize(new AutoSlopeSettings());

            Assert.Contains("\"SettingsVersion\":" + AutoSlopeSettings.CurrentSettingsVersion, json);
        }

        [Fact]
        public void BrokenFile_GivesDefaults()
        {
            string v001 = Write("settings.v001.json", "{ not json");

            AutoSlopeSettings s = AutoSlopeSettingsStore.LoadFrom(v001);

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

        [Fact]
        public void DefaultExportFolder_IsFloorSlopeReports()
        {
            Assert.Equal("FloorSlope_Reports", AutoSlopeConstants.DefaultExportFolder);
        }
    }
}
