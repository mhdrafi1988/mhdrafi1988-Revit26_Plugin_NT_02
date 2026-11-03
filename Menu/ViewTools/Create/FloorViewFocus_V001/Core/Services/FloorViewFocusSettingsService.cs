using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Services
{
    /// <summary>
    /// Load/save of %AppData%\Revit26_Plugin_Revised\FloorViewFocus\settings.json.
    /// Never throws — falls back to defaults and reports a warning string.
    /// </summary>
    public static class FloorViewFocusSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", FloorViewFocusDefaults.ToolName, "settings.json");

        public static FloorViewFocusSettings Load(out string? warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(SettingsPath))
                    return new FloorViewFocusSettings();

                var s = JsonSerializer.Deserialize<FloorViewFocusSettings>(File.ReadAllText(SettingsPath))
                        ?? new FloorViewFocusSettings();

                if (!IsValid(s.ViewMarginMm)) s.ViewMarginMm = FloorViewFocusDefaults.DefaultMarginMm;
                if (!IsValid(s.AnnotationMarginMm)) s.AnnotationMarginMm = FloorViewFocusDefaults.DefaultMarginMm;
                return s;
            }
            catch (Exception ex)
            {
                warning = $"Settings could not be loaded, defaults used: {ex.Message}";
                return new FloorViewFocusSettings();
            }
        }

        public static bool Save(FloorViewFocusSettings settings, out string? warning)
        {
            warning = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
                return true;
            }
            catch (Exception ex)
            {
                warning = $"Settings could not be saved: {ex.Message}";
                return false;
            }
        }

        private static bool IsValid(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;
    }
}
