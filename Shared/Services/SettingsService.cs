using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Generic settings load/save helper, per suite convention:
    /// %AppData%\Revit26_Plugin_Revised\[ToolName]\settings.json (System.Text.Json POCO).
    /// Load on window open, save on window close and after each Run.
    /// </summary>
    public static class SettingsService<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private static string GetSettingsPath(string toolName)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "Revit26_Plugin_Revised", toolName);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "settings.json");
        }

        /// <summary>Loads settings from disk, or returns a new default instance if the file doesn't exist or can't be read/parsed (corrupt file, schema change, etc. — never throws, always returns something usable).</summary>
        public static T Load(string toolName)
        {
            try
            {
                string path = GetSettingsPath(toolName);
                if (!File.Exists(path))
                    return new T();

                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<T>(json, JsonOptions);
                return loaded ?? new T();
            }
            catch
            {
                // Corrupt file, permission issue, schema mismatch after an
                // update, etc. — fall back to defaults rather than blocking
                // the tool from opening at all.
                return new T();
            }
        }

        /// <summary>Saves settings to disk. Silently no-ops on failure (e.g. permission issue) rather than interrupting the user's workflow with a dialog.</summary>
        public static void Save(string toolName, T settings)
        {
            try
            {
                string path = GetSettingsPath(toolName);
                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Flagged: silent failure on save. No log entry raised here
                // since this helper has no access to the tool's log
                // collection — callers that want a log line on failure
                // should wrap this call and add one themselves.
            }
        }
    }
}
