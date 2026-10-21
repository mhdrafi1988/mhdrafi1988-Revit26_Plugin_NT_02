using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Models;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Services
{
    /// <summary>
    /// Loads/saves ToolSettings as JSON at
    /// %AppData%\Revit26_Plugin_Revised\LinkedDetailLineGenerator\settings.json
    /// (no version segment, per suite convention).
    ///
    /// All failures are swallowed and logged via the onLog callback rather than thrown —
    /// a missing/corrupt settings file must never block the tool from opening.
    /// </summary>
    public class SettingsService
    {
        // VA007 gets its own subfolder (not shared with VA006) — its ToolSettings
        // carries fields VA006 doesn't have (RestrictToBoundary), so the two tools
        // must not read/write the same settings.json.
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "Revit26_Plugin_Revised", "LinkedDetailLineGenerator_VA007");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>
        /// Loads settings from disk. Returns a fresh default ToolSettings if the file
        /// doesn't exist or fails to parse (never throws to the caller).
        /// </summary>
        public ToolSettings Load(Action<string>? onLog = null)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    onLog?.Invoke($"No existing settings file — using defaults ({SettingsPath})");
                    return new ToolSettings();
                }

                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<ToolSettings>(json, JsonOptions);
                onLog?.Invoke($"Settings loaded from {SettingsPath}");
                return settings ?? new ToolSettings();
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Failed to load settings, using defaults: {ex.Message}");
                return new ToolSettings();
            }
        }

        /// <summary>
        /// Saves settings to disk, creating the directory if needed.
        /// Returns true on success; failures are logged, never thrown.
        /// </summary>
        public bool Save(ToolSettings settings, Action<string>? onLog = null)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsPath, json);
                onLog?.Invoke($"Settings saved to {SettingsPath}");
                return true;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Failed to save settings: {ex.Message}");
                return false;
            }
        }
    }
}
