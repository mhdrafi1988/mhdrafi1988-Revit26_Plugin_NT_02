using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V006
{
    /// <summary>
    /// Loads/saves RoofEdgeSectionsSettings to
    /// %AppData%\Revit26_Plugin_Revised\RoofEdgeSections\settings.json
    /// per the shared per-tool settings convention.
    /// </summary>
    public static class SettingsService
    {
        // Separate folder from V003's — the settings schema (below/above/far-length/margin
        // fields vs. offset/search-distance/edge-depth) is unrelated, so the two versions
        // must never read or overwrite each other's settings.json.
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "RoofEdgeSections_V005");

        private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static RoofEdgeSectionsSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new RoofEdgeSectionsSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<RoofEdgeSectionsSettings>(json, JsonOptions)
                       ?? new RoofEdgeSectionsSettings();
            }
            catch
            {
                // Corrupt or unreadable settings file — fall back to defaults rather than crash.
                return new RoofEdgeSectionsSettings();
            }
        }

        public static void Save(RoofEdgeSectionsSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Non-fatal — settings persistence failure should never block the tool.
            }
        }
    }
}
