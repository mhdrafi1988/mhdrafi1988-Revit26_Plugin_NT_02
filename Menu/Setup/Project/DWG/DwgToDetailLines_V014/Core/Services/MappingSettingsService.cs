// ==============================================
// File: MappingSettingsService.cs
// Layer: Core/Services
// Remembers the mapping mode, the two global picks and every per-layer pick
// between sessions, keyed by CAD layer name. Versioned JSON; a missing,
// unreadable or newer file is ignored and the old behaviour applies.
// ==============================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Models;

namespace Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Services
{
    /// <summary>Persisted mapping choices (file format version 1).</summary>
    public class MappingSettings
    {
        /// <summary>File format version, for future migration.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Single (global) or Multiple (per layer).</summary>
        public MappingMode Mode { get; set; } = MappingMode.Single;

        /// <summary>Global line style, or the match-layer-name entry.</summary>
        public string GlobalLineStyle { get; set; } = LayerRow.MatchLayerName;

        /// <summary>Global hatch (filled region type), or the match-layer-name entry.</summary>
        public string GlobalFillType { get; set; } = LayerRow.MatchLayerName;

        /// <summary>Per-layer line style picks (Multiple mode).</summary>
        public Dictionary<string, string> LineOverrides { get; set; } = new();

        /// <summary>Per-layer hatch picks (Multiple mode).</summary>
        public Dictionary<string, string> HatchOverrides { get; set; } = new();
    }

    /// <summary>Loads and saves <see cref="MappingSettings"/> under %AppData%\Revit26_Plugin_Revised.</summary>
    public static class MappingSettingsService
    {
        private const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "DwgToDlp", "mapping.json");

        /// <summary>Returns the saved settings, or defaults (Single mode, match layer name) when none can be read.</summary>
        public static MappingSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new MappingSettings();

                var loaded = JsonSerializer.Deserialize<MappingSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded == null || loaded.Version > CurrentVersion)
                    return new MappingSettings();

                loaded.LineOverrides ??= new();
                loaded.HatchOverrides ??= new();
                return loaded;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return new MappingSettings();
            }
        }

        /// <summary>Saves the settings; returns an error message, or null on success.</summary>
        public static string Save(MappingSettings settings)
        {
            try
            {
                settings.Version = CurrentVersion;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }
    }
}
