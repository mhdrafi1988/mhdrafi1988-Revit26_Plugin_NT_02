// =======================================================
// File: PresetStore.cs
// Location: Core/Models/
// Named sets of the window's options, saved next to settings.json in
// %AppData%\Revit26_Plugin_Revised\RoofPointsAndSlope\presets.json (with a schema
// version). A missing or unreadable file gives an empty list.
// =======================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>One named set of options.</summary>
    public sealed class SettingsPreset
    {
        /// <summary>Name shown in the list.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The options.</summary>
        public PipelineSettings Settings { get; set; } = new PipelineSettings();

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>The presets file.</summary>
    public sealed class PresetStore
    {
        /// <summary>Current file format.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>File format version.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>The presets, in the order shown.</summary>
        public List<SettingsPreset> Presets { get; set; } = new List<SettingsPreset>();

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        /// <summary>Full path of presets.json for <paramref name="toolFolder"/>.</summary>
        public static string FilePath(string toolFolder) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Revit26_Plugin_Revised", toolFolder, "presets.json");

        /// <summary>Loads the presets (empty when there is no file). <paramref name="error"/> is set when the file can't be read.</summary>
        public static PresetStore Load(string toolFolder, out string error)
        {
            error = null;
            string path = FilePath(toolFolder);
            try
            {
                if (!File.Exists(path)) return new PresetStore();
                PresetStore store = JsonSerializer.Deserialize<PresetStore>(File.ReadAllText(path), Json) ?? new PresetStore();
                store.Presets = (store.Presets ?? new List<SettingsPreset>())
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                    .ToList();
                foreach (SettingsPreset p in store.Presets)
                    p.Settings = (p.Settings ?? new PipelineSettings()).Normalize();
                store.SchemaVersion = CurrentSchemaVersion;
                return store;
            }
            catch (Exception ex)
            {
                error = $"The presets file could not be read ({ex.Message}): {path}";
                return new PresetStore();
            }
        }

        /// <summary>Saves the presets. Returns null, or the reason it failed.</summary>
        public string Save(string toolFolder)
        {
            string path = FilePath(toolFolder);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SchemaVersion = CurrentSchemaVersion;
                File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
                return null;
            }
            catch (Exception ex)
            {
                return $"The presets could not be saved ({ex.Message}): {path}";
            }
        }
    }
}
