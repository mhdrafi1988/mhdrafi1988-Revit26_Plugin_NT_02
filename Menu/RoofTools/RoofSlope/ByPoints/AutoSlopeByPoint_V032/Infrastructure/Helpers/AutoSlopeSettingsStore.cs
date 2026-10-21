// =======================================================
// File: AutoSlopeSettingsStore.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.Helpers
// (Named AutoSlopeSettingsStore, not SettingsService, to avoid clashing with
// Shared.Services.SettingsService where both namespaces are imported.)
// Load/save AutoSlopeSettings as JSON (try/catch, defaults on failure —
// CLAUDE.md convention). V032 uses its own file name so V030, kept as the
// fallback, never reads a file with a different shape:
//   %AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint\settings.v032.json
// =======================================================

using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Infrastructure.Helpers
{
    public static class AutoSlopeSettingsStore
    {
        public static string SettingsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AutoSlopeByPoint", "settings.v032.json");

        public static AutoSlopeSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AutoSlopeSettings();
                return JsonSerializer.Deserialize<AutoSlopeSettings>(File.ReadAllText(SettingsPath))
                       ?? new AutoSlopeSettings();
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
