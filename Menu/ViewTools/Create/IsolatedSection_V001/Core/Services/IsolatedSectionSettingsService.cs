using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Services
{
    /// <summary>
    /// Load/save of %AppData%\Revit26_Plugin\IsolatedSection\settings.json.
    /// Never throws: falls back to defaults and reports a warning string.
    /// </summary>
    public static class IsolatedSectionSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Full path of the settings file.</summary>
        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin", "IsolatedSection", "settings.json");

        /// <summary>Reads the settings, or defaults when the file is missing or unreadable.</summary>
        public static IsolatedSectionSettings Load(out string warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(SettingsPath)) return new IsolatedSectionSettings();

                var s = JsonSerializer.Deserialize<IsolatedSectionSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                        ?? new IsolatedSectionSettings();
                if (s.SchemaVersion > IsolatedSectionSettings.CurrentSchemaVersion)
                    warning = $"Settings were saved by a newer version (format {s.SchemaVersion}); unknown options were ignored.";

                var defaults = new IsolatedSectionSettings();
                if (!IsValidMm(s.SideMarginMm)) s.SideMarginMm = defaults.SideMarginMm;
                if (!IsValidMm(s.TopBottomMarginMm)) s.TopBottomMarginMm = defaults.TopBottomMarginMm;
                if (!IsValidMm(s.DepthMarginMm)) s.DepthMarginMm = defaults.DepthMarginMm;
                if (s.Side == SideChoice.PickedFace || !Enum.IsDefined(s.Side)) s.Side = SideChoice.Auto;
                if (!Enum.IsDefined(s.Isolation)) s.Isolation = defaults.Isolation;
                if (!Enum.IsDefined(s.Camera)) s.Camera = defaults.Camera;
                if (!Enum.IsDefined(s.Detail)) s.Detail = defaults.Detail;
                if (!Enum.IsDefined(s.Style)) s.Style = defaults.Style;
                s.NamePattern ??= defaults.NamePattern;
                s.ViewTypeName ??= string.Empty;
                s.TemplateName ??= string.Empty;
                s.SchemaVersion = IsolatedSectionSettings.CurrentSchemaVersion;
                return s;
            }
            catch (Exception ex)
            {
                warning = $"Settings could not be loaded, defaults used: {ex.Message}";
                return new IsolatedSectionSettings();
            }
        }

        /// <summary>Writes the settings; returns false (with a warning) on failure.</summary>
        public static bool Save(IsolatedSectionSettings settings, out string warning)
        {
            warning = null;
            try
            {
                settings.SchemaVersion = IsolatedSectionSettings.CurrentSchemaVersion;
                if (settings.Side == SideChoice.PickedFace) settings.Side = SideChoice.Auto;
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
                return true;
            }
            catch (Exception ex)
            {
                warning = $"Settings could not be saved: {ex.Message}";
                return false;
            }
        }

        private static bool IsValidMm(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0 && v <= 100000;
    }
}
