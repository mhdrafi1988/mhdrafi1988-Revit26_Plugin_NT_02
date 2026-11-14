// =======================================================
// File: AutoSlopeSettingsStore.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Infrastructure.Helpers
// (Named AutoSlopeSettingsStore, not SettingsService, to avoid clashing with
// Shared.Services.SettingsService where both namespaces are imported.)
// Load/save AutoSlopeSettings as JSON (try/catch, defaults on failure —
// CLAUDE.md convention). Floor Slope By Point has its own folder, so it never
// shares settings with the roof tool:
//   %AppData%\Revit26_Plugin_Revised\FloorSlopeByPoint\settings.v001.json
// The file carries SettingsVersion; a later version reads this one and fills
// missing fields with defaults.
// =======================================================

using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Infrastructure.Helpers
{
    public static class AutoSlopeSettingsStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "FloorSlopeByPoint");

        /// <summary>V001's settings file.</summary>
        public static string SettingsPath { get; } = Path.Combine(Folder, "settings.v001.json");

        /// <summary>Loads V001's settings, else the defaults. Never throws.</summary>
        public static AutoSlopeSettings Load() => LoadFrom(SettingsPath);

        /// <summary>
        /// Reads the first file in <paramref name="paths"/> that exists. A file that can't be read
        /// gives the defaults (it is not skipped for the next one, so a broken newer file never
        /// silently brings back older values). Never throws.
        /// </summary>
        public static AutoSlopeSettings LoadFrom(params string[] paths)
        {
            try
            {
                foreach (string path in paths ?? Array.Empty<string>())
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    return JsonSerializer.Deserialize<AutoSlopeSettings>(File.ReadAllText(path))
                           ?? new AutoSlopeSettings();
                }
                return new AutoSlopeSettings();
            }
            catch
            {
                return new AutoSlopeSettings();
            }
        }

        /// <summary>Writes the settings with the current SettingsVersion. Never throws; false on failure.</summary>
        public static bool Save(AutoSlopeSettings settings)
        {
            try
            {
                if (settings == null) return false;
                settings.SettingsVersion = AutoSlopeSettings.CurrentSettingsVersion;
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
