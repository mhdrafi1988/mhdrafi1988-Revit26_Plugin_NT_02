using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V005
{
    /// <summary>
    /// Loads/saves TagPlacementSettings to
    /// %AppData%\Revit26_Plugin_Revised\SectionViewAutoTagger\settings.json.
    /// Tool-name-scoped (not version-scoped) per suite convention. Worklist
    /// contents are session-only and NOT persisted here.
    /// </summary>
    public class SettingsService
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Revit26_Plugin_Revised", "SectionViewAutoTagger");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        public TagPlacementSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new TagPlacementSettings();

                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<TagPlacementSettings>(json);
                return settings ?? new TagPlacementSettings();
            }
            catch
            {
                // Corrupt or unreadable settings file — fall back to defaults
                // rather than crash the window on open.
                return new TagPlacementSettings();
            }
        }

        public bool Save(TagPlacementSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
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
