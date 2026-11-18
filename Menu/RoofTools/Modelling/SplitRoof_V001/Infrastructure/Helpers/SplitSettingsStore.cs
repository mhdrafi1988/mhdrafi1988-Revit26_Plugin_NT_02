// Split Roof V001 — loads/saves SplitSettings as versioned JSON under %AppData%\Revit26_Plugin_Revised\SplitRoof.
using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Infrastructure.Helpers
{
    /// <summary>Persists the Split Roof settings.</summary>
    public static class SplitSettingsStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "SplitRoof", "settings.json");

        /// <summary>Loads the settings; a missing or unreadable file gives the defaults (logged to the debug output, never shown).</summary>
        public static SplitSettings Load()
        {
            try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<SplitSettings>(File.ReadAllText(FilePath)) ?? new SplitSettings() : new SplitSettings(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("SplitRoof settings unreadable: " + ex.Message); return new SplitSettings(); }
        }

        /// <summary>Saves the settings; failure is non-fatal and logged to the debug output.</summary>
        public static void Save(SplitSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("SplitRoof settings not saved: " + ex.Message); }
        }
    }
}
