using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Loads/saves FloorEdgeSectionsSettings to
    /// %AppData%\Revit26_Plugin_Revised\FloorEdgeAroundSections\settings.json
    /// per the shared per-tool settings convention.
    /// </summary>
    public static class SettingsService
    {
        // Separate folder from V003's — the settings schema (below/above/far-length/margin
        // fields vs. offset/search-distance/edge-depth) is unrelated, so the two versions
        // must never read or overwrite each other's settings.json.
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "FloorEdgeAroundSections");

        private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static FloorEdgeSectionsSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new FloorEdgeSectionsSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<FloorEdgeSectionsSettings>(json, JsonOptions)
                       ?? new FloorEdgeSectionsSettings();
            }
            catch
            {
                // Corrupt or unreadable settings file — fall back to defaults rather than crash.
                return new FloorEdgeSectionsSettings();
            }
        }

        public static void Save(FloorEdgeSectionsSettings settings)
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
