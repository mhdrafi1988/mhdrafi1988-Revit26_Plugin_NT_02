using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V012
{
    /// <summary>
    /// Loads/saves ToolSettings as JSON. All failures are non-fatal: a corrupt or
    /// missing file simply yields fresh defaults, and a failed save is reported back
    /// via the out message so the ViewModel can log it — the tool never blocks on
    /// settings I/O.
    /// </summary>
    public static class SettingsService
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "FloorsAndRoofFromLinkedRooms");

        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>Returns loaded settings, or a fresh instance when no/invalid file exists.
        /// The out message describes the outcome for the UI log.</summary>
        public static ToolSettings Load(out string message)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    message = "No saved settings found — using defaults.";
                    return new ToolSettings();
                }

                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<ToolSettings>(json);
                if (settings == null)
                {
                    message = "Settings file was empty — using defaults.";
                    return new ToolSettings();
                }

                message = $"Settings loaded from {FilePath}";
                return settings;
            }
            catch (Exception ex)
            {
                message = $"Could not read settings ({ex.Message}) — using defaults.";
                return new ToolSettings();
            }
        }

        /// <summary>Saves settings; returns false (with reason in message) on failure.</summary>
        public static bool Save(ToolSettings settings, out string message)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
                message = $"Settings saved to {FilePath}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Could not save settings: {ex.Message}";
                return false;
            }
        }
    }
}
