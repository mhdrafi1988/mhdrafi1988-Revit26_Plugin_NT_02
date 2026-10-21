// File: SettingsService.cs
// Location: Infrastructure/Helpers/
// NEW (V005). Neither AutoSlopeByPoint V018 nor AutoSlopeByDrain V004 had
// settings persistence, despite it being a stated project convention —
// confirmed as a real gap during the merge audit. Rafi confirmed adding it
// for this tool. Loads on window open, saves after a successful Run and on
// window close.

using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Infrastructure.Helpers
{
    /// <summary>Ported from AutoSlopeByPoint's CircleMarkerGroupSettings — persisted by Line Style NAME, not ElementId (project-specific, not portable).</summary>
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
        public bool VerifyElevationsAfterCommit { get; set; } = false;
        public string ExportFolderPath { get; set; } = string.Empty;
        public string SelectedSizeFilter { get; set; } = "All";

        // ── Circle Markers (ported from AutoSlopeByPoint) ────────────────
        public CircleMarkerGroupSettings DrainMarkerGroup { get; set; } = new CircleMarkerGroupSettings { ColorName = "Blue" };
        public CircleMarkerGroupSettings HighestPointMarkerGroup { get; set; } = new CircleMarkerGroupSettings { ColorName = "Red" };
        public CircleMarkerGroupSettings AllowedOffsetMarkerGroup { get; set; } = new CircleMarkerGroupSettings { ColorName = "Orange" };
        public double AllowedOffsetThresholdMm { get; set; } = 500;
    }

    public static class SettingsService
    {
        private const string ToolFolderName = "AutoSlopeByDrain_V010";

        private static string SettingsFilePath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "Revit26_Plugin_Revised", ToolFolderName);
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "settings.json");
            }
        }

        /// <summary>
        /// Loads settings from disk. Returns a settings object populated with
        /// AppConstants defaults if the file doesn't exist yet or fails to parse
        /// (e.g. corrupted file from a future version's schema change) — never
        /// throws back to the caller.
        /// </summary>
        public static AutoSlopeDrainSettings Load(Action<string> logAction = null)
        {
            try
            {
                string path = SettingsFilePath;
                if (!File.Exists(path))
                    return new AutoSlopeDrainSettings();

                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AutoSlopeDrainSettings>(json);
                return settings ?? new AutoSlopeDrainSettings();
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Could not load saved settings, using defaults: {ex.Message}");
                return new AutoSlopeDrainSettings();
            }
        }

        /// <summary>
        /// Saves settings to disk. Failures are logged (if a log action is
        /// supplied) but never thrown — a settings-save failure should never
        /// interrupt a Run or block the window from closing.
        /// </summary>
        public static void Save(AutoSlopeDrainSettings settings, Action<string> logAction = null)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Could not save settings: {ex.Message}");
            }
        }
    }
}
