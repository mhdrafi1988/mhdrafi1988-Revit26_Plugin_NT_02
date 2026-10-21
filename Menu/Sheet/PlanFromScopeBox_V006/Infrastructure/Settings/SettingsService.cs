using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.PlanFromScopeBox.V006.Infrastructure.Settings
{
    /// <summary>
    /// Loads/saves per-tool settings JSON and resolves the tool's AppData folder. All I/O is
    /// defensive: failures fall back to defaults and never throw into the UI (H2). The same
    /// folder is reused for silent log auto-save (H3).
    /// </summary>
    public sealed class SettingsService
    {
        private const string ToolFolder = "PlanFromScopeBox";
        private const string FileName = "settings.json";

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        /// <summary>%AppData%\Revit26_Plugin_Revised\PlanFromScopeBox\ — created if missing.</summary>
        public string ToolDataDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(root, "Revit26_Plugin_Revised", ToolFolder);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private string SettingsPath => Path.Combine(ToolDataDirectory, FileName);

        public PlanFromScopeBoxSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new PlanFromScopeBoxSettings();
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<PlanFromScopeBoxSettings>(json)
                       ?? new PlanFromScopeBoxSettings();
            }
            catch
            {
                return new PlanFromScopeBoxSettings();
            }
        }

        public void Save(PlanFromScopeBoxSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, JsonOpts);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Non-fatal — settings persistence is best-effort.
            }
        }
    }
}
