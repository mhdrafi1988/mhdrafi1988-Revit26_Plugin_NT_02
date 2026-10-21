using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.CreateSections.V012.Models;

namespace Revit26_Plugin_Revised.CreateSections.V012.Services
{
    /// <summary>
    /// V008: new. Loads/saves SectionCreationSettings to
    /// %AppData%\Revit26_Plugin_Revised\CreateSectionsFromDetailLines\settings.json,
    /// per suite convention.
    /// </summary>
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised",
            "CreateSectionsFromDetailLines",
            "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static SectionCreationSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new SectionCreationSettings();

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<SectionCreationSettings>(json, JsonOptions)
                       ?? new SectionCreationSettings();
            }
            catch
            {
                // Corrupt or unreadable settings file — fall back to defaults
                // rather than crashing the tool on open.
                return new SectionCreationSettings();
            }
        }

        public static void Save(SectionCreationSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Best-effort — a failed settings save should never block
                // the user from closing the dialog or finishing their work.
            }
        }
    }
}
