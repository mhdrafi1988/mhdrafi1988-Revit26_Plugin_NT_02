using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V004
{
    /// <summary>
    /// Loads/saves RoofEdgeElementSectionsSettings to
    /// %AppData%\Revit26_Plugin_Revised\RoofEdgeElementSections_V002\settings.json
    /// per the shared per-tool settings convention.
    /// </summary>
    public static class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "RoofEdgeElementSections_V002");

        private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static RoofEdgeElementSectionsSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new RoofEdgeElementSectionsSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<RoofEdgeElementSectionsSettings>(json, JsonOptions)
                       ?? new RoofEdgeElementSectionsSettings();
            }
            catch
            {
                return new RoofEdgeElementSectionsSettings();
            }
        }

        public static void Save(RoofEdgeElementSectionsSettings settings)
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
