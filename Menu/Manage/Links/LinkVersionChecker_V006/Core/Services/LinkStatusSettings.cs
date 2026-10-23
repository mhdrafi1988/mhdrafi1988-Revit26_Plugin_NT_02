using System;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Services
{
    /// <summary>
    /// Per-user settings of the V006 window, stored in
    /// <c>%AppData%\Revit26_Plugin_Revised\LinkVersionChecker\settings_v006.json</c>.
    /// Missing or unreadable files fall back to the defaults.
    /// </summary>
    public sealed class LinkStatusSettings
    {
        /// <summary>File format version. Bump when fields change meaning; unknown fields are ignored.</summary>
        public int Version { get; set; } = 1;

        /// <summary>How "Folder in ACC" is shortened. Default: last N folders.</summary>
        public FolderDisplayMode FolderMode { get; set; } = FolderDisplayMode.LastN;

        /// <summary>N for <see cref="FolderMode"/>. Default 2.</summary>
        public int FolderDepth { get; set; } = 2;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "LinkVersionChecker", "settings_v006.json");

        /// <summary>Loads the settings, or the defaults when the file is missing or invalid. Never throws.</summary>
        public static LinkStatusSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new LinkStatusSettings();
                var s = JsonSerializer.Deserialize<LinkStatusSettings>(File.ReadAllText(FilePath)) ?? new LinkStatusSettings();
                if (!Enum.IsDefined(typeof(FolderDisplayMode), s.FolderMode)) s.FolderMode = FolderDisplayMode.LastN;
                s.FolderDepth = Math.Clamp(s.FolderDepth, FolderDisplay.MinDepth, FolderDisplay.MaxDepth);
                return s;
            }
            catch
            {
                return new LinkStatusSettings();
            }
        }

        /// <summary>Saves the settings. Returns an error message, or null on success.</summary>
        public string Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
