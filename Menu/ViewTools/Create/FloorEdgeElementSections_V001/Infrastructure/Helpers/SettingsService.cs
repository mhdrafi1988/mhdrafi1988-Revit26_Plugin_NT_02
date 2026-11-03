using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Loads/saves FloorEdgeElementSectionsSettings to
    /// %AppData%\Revit26_Plugin_Revised\FloorEdgeElementSections\settings.json
    /// per the shared per-tool settings convention.
    /// </summary>
    public static class SettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "FloorEdgeElementSections");

        private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static FloorEdgeElementSectionsSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new FloorEdgeElementSectionsSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<FloorEdgeElementSectionsSettings>(json, JsonOptions)
                       ?? new FloorEdgeElementSectionsSettings();
            }
            catch
            {
                return new FloorEdgeElementSectionsSettings();
            }
        }

        public static void Save(FloorEdgeElementSectionsSettings settings)
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
