// =======================================================
// File: DrainSettingsStore.cs
// Namespace: Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Infrastructure.Helpers
// Changes vs V012 (SettingsService.cs):
//   - Renamed so it cannot clash with Shared.Services.SettingsService.
//   - New file: %AppData%\Revit26_Plugin_Revised\AutoSlopeByDrain\settings.v013.json
//     (V012 wrote to an "AutoSlopeByDrain_V010" folder).
//   - Adds AskToOpenAfterExport and which option sections were left open.
//   - Read-back after the run (VerifyElevationsAfterCommit) is on by default:
//     it now uses a lookup instead of comparing every point with every other
//     point, so it no longer slows large roofs.
// =======================================================

using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Infrastructure.Helpers
{
    /// <summary>Persisted by Line Style NAME, not ElementId (ids are project-specific).</summary>
    public class CircleMarkerGroupSettings
    {
        public bool IsEnabled { get; set; } = true;
        public string LineStyleName { get; set; }
        public string ColorName { get; set; } = "Black";
        public double RadiusMm { get; set; } = 250;
        public bool ShowOffsetText { get; set; } = true;
    }

    public class AutoSlopeDrainSettings
    {
        public double SlopePercent { get; set; } = AppConstants.DefaultSlopePercent;
        public double ConnectionThresholdMeters { get; set; } = AppConstants.DefaultConnectionThresholdMeters;
        public double ThresholdMeters { get; set; } = AppConstants.DefaultThresholdMeters;
        public int PathSampleCount { get; set; } = AppConstants.DefaultPathSampleCount;
        public bool InsertCurveIntersectionPoints { get; set; } = false;
        public bool VerifyElevationsAfterCommit { get; set; } = true;
        public string ExportFolderPath { get; set; } = string.Empty;
        public string SelectedSizeFilter { get; set; } = "All";
        public bool WriteCombinedSummary { get; set; } = true;
        public bool AskToOpenAfterExport { get; set; } = true;

        public CircleMarkerGroupSettings DrainMarkerGroup { get; set; } = new CircleMarkerGroupSettings { ColorName = AppConstants.DefaultDrainColor };
        public CircleMarkerGroupSettings HighestPointMarkerGroup { get; set; } = new CircleMarkerGroupSettings { ColorName = AppConstants.DefaultHighestColor };
        public CircleMarkerGroupSettings AllowedOffsetMarkerGroup { get; set; } = new CircleMarkerGroupSettings { ColorName = AppConstants.DefaultOffsetColor };
        public double AllowedOffsetThresholdMm { get; set; } = AppConstants.DefaultAllowedOffsetMm;

        // Option sections left open (all closed by default)
        public bool PathOptionsExpanded { get; set; }
        public bool MarkersExpanded { get; set; }
        public bool ExportExpanded { get; set; }
    }

    public static class DrainSettingsStore
    {
        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AutoSlopeByDrain", "settings.v013.json");

        /// <summary>Never throws: a missing or unreadable file gives the defaults.</summary>
        public static AutoSlopeDrainSettings Load(Action<string> logAction = null)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AutoSlopeDrainSettings();
                return JsonSerializer.Deserialize<AutoSlopeDrainSettings>(File.ReadAllText(SettingsPath))
                       ?? new AutoSlopeDrainSettings();
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Saved settings could not be read, so the defaults are used ({ex.Message}).");
                return new AutoSlopeDrainSettings();
            }
        }

        /// <summary>Never throws. False when the file could not be written.</summary>
        public static bool Save(AutoSlopeDrainSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
