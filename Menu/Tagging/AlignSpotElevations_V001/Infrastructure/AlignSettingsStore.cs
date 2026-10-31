using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure
{
    /// <summary>
    /// Loads and saves the last-used options in
    /// <c>%AppData%\Revit26_Plugin_Revised\AlignSpotElevations\settings.json</c>. A missing, unreadable or
    /// newer file falls back to defaults; the tool never fails because of its settings.
    /// </summary>
    public static class AlignSettingsStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Full path of the settings file.</summary>
        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AlignSpotElevations", "settings.json");

        /// <summary>Last-used options, or defaults.</summary>
        public static AlignOptions Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AlignOptions();
                AlignOptions o = JsonSerializer.Deserialize<AlignOptions>(File.ReadAllText(FilePath), Json);
                if (o == null || o.Version > AlignOptions.CurrentVersion) return new AlignOptions();
                o.Version = AlignOptions.CurrentVersion;
                if (o.ToleranceMm < 0) o.ToleranceMm = 0;
                if (o.GapMm < 0) o.GapMm = 0;
                return o;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlignSpotElevations] settings not loaded: {ex.Message}");
                return new AlignOptions();
            }
        }

        /// <summary>Saves <paramref name="options"/>; failures are ignored (logged to debug output).</summary>
        public static void Save(AlignOptions options)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(options, Json));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlignSpotElevations] settings not saved: {ex.Message}");
            }
        }
    }
}
