// =======================================================
// File: AutoSlopeSettingsStore.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Helpers
// (Named AutoSlopeSettingsStore, not SettingsService, to avoid clashing with
// Shared.Services.SettingsService where both namespaces are imported.)
// Load/save AutoSlopeSettings as JSON (try/catch, defaults on failure —
// CLAUDE.md convention). Each version has its own file so the versions kept
// on the ribbon as fallbacks never read a file with a different shape:
//   %AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint\settings.v034.json
// V034 adds fields only. Until V034 has saved once, it reads V033's file
// (settings.v033.json); the new fields are missing there and read as off.
// =======================================================

using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Helpers
{
    public static class AutoSlopeSettingsStore
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AutoSlopeByPoint");

        /// <summary>V034's own settings file.</summary>
        public static string SettingsPath { get; } = Path.Combine(Folder, "settings.v034.json");

        /// <summary>V033's file, read once until V034 saves its own.</summary>
        public static string PreviousSettingsPath { get; } = Path.Combine(Folder, "settings.v033.json");

        /// <summary>Loads V034's settings, else V033's, else the defaults. Never throws.</summary>
        public static AutoSlopeSettings Load() => LoadFrom(SettingsPath, PreviousSettingsPath);

        /// <summary>
        /// Reads the first file in <paramref name="paths"/> that exists. A file that can't be read
        /// gives the defaults (it is not skipped for the next one, so a broken V033 file never
        /// silently brings back older V032 values). Never throws.
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

        public static bool Save(AutoSlopeSettings settings)
        {
            try
            {
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
