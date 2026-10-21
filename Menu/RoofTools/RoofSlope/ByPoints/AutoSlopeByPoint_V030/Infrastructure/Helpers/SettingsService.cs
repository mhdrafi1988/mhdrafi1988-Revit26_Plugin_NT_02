// =======================================================
// File: SettingsService.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V030
// New in V026.
// Purpose: Load/save AutoSlopeSettings as JSON at
//          %AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint\settings.json
//          per this suite's shared settings-path convention.
// =======================================================

using Revit26_Plugin_Revised.AutoSlopeByPoint.V030.Core.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V030.Infrastructure.Helpers
{
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AutoSlopeByPoint", "settings.json");

        /// <summary>
        /// Loads settings from disk. Returns a fresh default instance (never null)
        /// if the file doesn't exist yet or fails to parse — a missing/corrupt
        /// settings file should never block the tool from opening.
        /// </summary>
        public static AutoSlopeSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AutoSlopeSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AutoSlopeSettings>(json) ?? new AutoSlopeSettings();
            }
            catch
            {
                // Corrupt or unreadable settings file — fall back to defaults
                // rather than surfacing a startup error to the user.
                return new AutoSlopeSettings();
            }
        }

        /// <summary>
        /// Saves settings to disk, creating the target directory if needed.
        /// Best-effort — a failure to save should not interrupt the user's flow,
        /// so callers should treat this as fire-and-forget with logging only.
        /// </summary>
        public static bool Save(AutoSlopeSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
