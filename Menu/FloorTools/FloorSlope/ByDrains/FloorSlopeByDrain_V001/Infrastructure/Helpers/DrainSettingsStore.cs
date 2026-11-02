// =======================================================
// File: DrainSettingsStore.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
// Floor port of Auto Slope By Drain V013's settings store.
//   - File: %AppData%\Revit26_Plugin_Revised\FloorSlopeByDrain\settings.v001.json
//     (its own folder, so it never shares settings with the roof tool).
//   - SettingsVersion is written with the file; fields missing from an older
//     file read as their defaults.
// =======================================================

using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.Helpers
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

    /// <summary>Saved window settings for Floor Slope By Drain.</summary>
    public class AutoSlopeDrainSettings
    {
        /// <summary>Format version of the settings file this tool writes.</summary>
        public const int CurrentSettingsVersion = 1;

        /// <summary>Format version of this file (1 = Floor Slope By Drain V001).</summary>
        public int SettingsVersion { get; set; } = CurrentSettingsVersion;

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

    /// <summary>Loads and saves <see cref="AutoSlopeDrainSettings"/> as JSON. Never throws.</summary>
    public static class DrainSettingsStore
    {
        /// <summary>Full path of the settings file.</summary>
        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "FloorSlopeByDrain", "settings.v001.json");

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
                if (settings == null) return false;
                settings.SettingsVersion = AutoSlopeDrainSettings.CurrentSettingsVersion;
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
