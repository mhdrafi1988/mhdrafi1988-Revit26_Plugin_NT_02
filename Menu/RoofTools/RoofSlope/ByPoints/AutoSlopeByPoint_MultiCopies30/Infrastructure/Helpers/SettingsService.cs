// =======================================================
// File: SettingsService.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30
// Purpose: Load/save AutoSlopeSettings as JSON at
//          %AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint_MultiCopies28\settings.json
//          — a separate file/folder from V028's settings.json, since
//          the schema (SlopeRows list vs single SlopePercent) is
//          incompatible and sharing the file would corrupt either
//          version's saved settings on load.
// =======================================================

using Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30.Core.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30.Infrastructure.Helpers
{
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AutoSlopeByPoint_MultiCopies28", "settings.json");

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
                var settings = JsonSerializer.Deserialize<AutoSlopeSettings>(json) ?? new AutoSlopeSettings();
                EnsureFourSlopeRows(settings);
                return settings;
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

        /// <summary>
        /// The UI indexes SlopeRows[0..3] directly for its 4 fixed rows — pad a
        /// missing/short list (older or hand-edited settings.json) with disabled
        /// default rows so a partial file never throws instead of just falling
        /// back for the rows it's missing.
        /// </summary>
        private static void EnsureFourSlopeRows(AutoSlopeSettings settings)
        {
            settings.SlopeRows ??= new System.Collections.Generic.List<Core.Models.SlopeRowSetting>();
            for (int i = settings.SlopeRows.Count; i < 4; i++)
            {
                settings.SlopeRows.Add(new Core.Models.SlopeRowSetting
                {
                    RowIndex = i + 1,
                    IsEnabled = false,
                    Percent = i + 1
                });
            }
        }
    }
}
