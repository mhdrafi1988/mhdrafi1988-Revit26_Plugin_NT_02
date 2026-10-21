// ==============================================
// File: WorksetSettingsService.cs
// Layer: Core/Services  (no Revit API, no UI — CLAUDE.md)
// NEW in Workset Creator V016.
//
// Loads and saves WorksetCreatorSettings as JSON at
//   %AppData%\Revit26_Plugin_Revised\WorksetCreator\settings.json
//
// Both directions are wrapped: a missing file, a corrupt file, a locked file or
// a denied folder all fall back to defaults and log a Warning. Settings are a
// convenience, never load-bearing — the window must open whatever the state of
// the disk, so nothing in here is allowed to throw at the caller.
// ==============================================

using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.WorksetCreator.V016.Core.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.WorksetCreator.V016.Core.Services
{
    public sealed class WorksetSettingsService
    {
        private const string VendorFolder = "Revit26_Plugin_Revised";
        private const string ToolFolder   = "WorksetCreator";
        private const string FileName     = "settings.json";

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip
        };

        private readonly Action<LogEntry> _log;

        public WorksetSettingsService(Action<LogEntry> logger) => _log = logger;

        /// <summary>Full path of the settings file, whether or not it exists.</summary>
        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            VendorFolder, ToolFolder, FileName);

        /// <summary>
        /// Reads the settings file. Never throws and never returns null: anything
        /// unreadable produces a defaults instance, with the reason logged once.
        /// </summary>
        public WorksetCreatorSettings Load()
        {
            var defaults = new WorksetCreatorSettings();

            try
            {
                string path = SettingsPath;

                if (!File.Exists(path))
                {
                    Log(LogLevel.Info, $"No settings file yet — using defaults ({path}).");
                    return defaults;
                }

                string json = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(json))
                {
                    Log(LogLevel.Warning, "Settings file is empty — using defaults.");
                    return defaults;
                }

                WorksetCreatorSettings loaded =
                    JsonSerializer.Deserialize<WorksetCreatorSettings>(json, ReadOptions);

                if (loaded == null)
                {
                    Log(LogLevel.Warning, "Settings file did not deserialise — using defaults.");
                    return defaults;
                }

                if (loaded.SchemaVersion > WorksetCreatorSettings.CurrentSchemaVersion)
                {
                    // Written by a newer build. Read what we understand rather than
                    // discarding it, but say so — the next save rewrites at our version.
                    Log(LogLevel.Warning,
                        $"Settings file is schema v{loaded.SchemaVersion}, this build understands " +
                        $"v{WorksetCreatorSettings.CurrentSchemaVersion} — unknown keys ignored.");
                }

                loaded.Sanitise();

                Log(LogLevel.Info,
                    $"Settings loaded: Pattern='{loaded.Pattern}', " +
                    $"ConventionMinOccurrences={loaded.ConventionMinOccurrences}, " +
                    $"MinNormalisedNameLength={loaded.MinNormalisedNameLength}");

                return loaded;
            }
            catch (JsonException ex)
            {
                Log(LogLevel.Warning, $"Settings file is not valid JSON — using defaults. {ex.Message}");
                return defaults;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Could not read settings — using defaults. {ex.Message}");
                return defaults;
            }
        }

        /// <summary>
        /// Writes the settings file, creating the folder if needed. Returns false and
        /// logs on failure; the caller carries on regardless.
        /// </summary>
        public bool Save(WorksetCreatorSettings settings)
        {
            if (settings == null) return false;

            try
            {
                settings.SchemaVersion = WorksetCreatorSettings.CurrentSchemaVersion;
                settings.Sanitise();

                string path   = SettingsPath;
                string folder = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllText(path, JsonSerializer.Serialize(settings, WriteOptions));
                return true;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Could not save settings. {ex.Message}");
                return false;
            }
        }

        private void Log(LogLevel level, string message) => _log?.Invoke(new LogEntry(level, message));
    }
}
