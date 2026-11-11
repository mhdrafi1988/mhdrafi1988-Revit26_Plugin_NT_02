// Merge Roofs V001 — loads/saves MergeSettings as versioned JSON under %AppData%\Revit26_Plugin_Revised\MergeRoofs.
using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers
{
    /// <summary>Persists the Merge Roofs settings.</summary>
    public static class MergeSettingsStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "MergeRoofs", "settings.json");

        /// <summary>Loads the settings; a missing or unreadable file gives the defaults (and is logged, never shown).</summary>
        public static MergeSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new MergeSettings();
                var s = JsonSerializer.Deserialize<MergeSettings>(File.ReadAllText(FilePath)) ?? new MergeSettings();
                if (!(s.ToleranceMm >= 0 && s.ToleranceMm <= 1000)) s.ToleranceMm = 5.0;
                return s;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MergeRoofs settings unreadable: " + ex.Message);
                return new MergeSettings();
            }
        }

        /// <summary>Saves the settings; failure is non-fatal and logged to the debug output.</summary>
        public static void Save(MergeSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MergeRoofs settings not saved: " + ex.Message);
            }
        }
    }
}
