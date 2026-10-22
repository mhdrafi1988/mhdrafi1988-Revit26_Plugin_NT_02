using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.RoofViewFocus.V003.Core.Models;

namespace Revit26_Plugin_Revised.RoofViewFocus.V003.Core.Services
{
    /// <summary>
    /// Load/save of %AppData%\Revit26_Plugin_Revised\RoofViewFocus\settings.json.
    /// Never throws — falls back to defaults and reports a warning string.
    /// </summary>
    public static class RoofViewFocusSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", RoofViewFocusDefaults.ToolName, "settings.json");

        public static RoofViewFocusSettings Load(out string? warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(SettingsPath))
                    return new RoofViewFocusSettings();

                var s = JsonSerializer.Deserialize<RoofViewFocusSettings>(File.ReadAllText(SettingsPath))
                        ?? new RoofViewFocusSettings();

                if (!IsValid(s.ViewMarginMm)) s.ViewMarginMm = RoofViewFocusDefaults.DefaultMarginMm;
                if (!IsValid(s.AnnotationMarginMm)) s.AnnotationMarginMm = RoofViewFocusDefaults.DefaultMarginMm;
                return s;
            }
            catch (Exception ex)
            {
                warning = $"Settings could not be loaded, defaults used: {ex.Message}";
                return new RoofViewFocusSettings();
            }
        }

        public static bool Save(RoofViewFocusSettings settings, out string? warning)
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
