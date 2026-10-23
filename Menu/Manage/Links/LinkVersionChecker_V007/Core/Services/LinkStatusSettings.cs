using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services
{
    /// <summary>
    /// Per-user settings of the V007 window, stored in
    /// <c>%AppData%\Revit26_Plugin_Revised\LinkVersionChecker\settings_v007.json</c>.
    /// The first time V007 runs it starts from the V006 file's folder setting. Missing or
    /// unreadable files fall back to the defaults.
    /// </summary>
    public sealed class LinkStatusSettings
    {
        /// <summary>File format version. Bump when fields change meaning; unknown fields are ignored.</summary>
        public int Version { get; set; } = 1;

        /// <summary>How "Folder in ACC" is shortened. Default: last N folders.</summary>
        public FolderDisplayMode FolderMode { get; set; } = FolderDisplayMode.LastN;

        /// <summary>N for <see cref="FolderMode"/>. Default 2.</summary>
        public int FolderDepth { get; set; } = 2;

        /// <summary>List links that are not in ACC as well. Default off (V006 never listed them).  [V007]</summary>
        public bool ShowOtherLinks { get; set; }

        /// <summary>
        /// Extra Desktop Connector ACCDocs folders to look in for the local copy of a cloud link,
        /// tried after <c>%USERPROFILE%\DC\ACCDocs</c> and <c>%USERPROFILE%\ACCDocs</c>.
        /// Environment variables are expanded. Default empty.  [7.1.0]
        /// </summary>
        public List<string> DesktopConnectorRoots { get; set; } = new();

        private static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "LinkVersionChecker");

        private static string FilePath => Path.Combine(Folder, "settings_v007.json");

        // V006 file: read once, only to carry its folder setting over.
        private static string V006FilePath => Path.Combine(Folder, "settings_v006.json");

        /// <summary>Loads the settings, or the defaults when the file is missing or invalid. Never throws.</summary>
        public static LinkStatusSettings Load()
        {
            try
            {
                string file = File.Exists(FilePath) ? FilePath : File.Exists(V006FilePath) ? V006FilePath : null;
                if (file == null) return new LinkStatusSettings();
                var s = JsonSerializer.Deserialize<LinkStatusSettings>(File.ReadAllText(file)) ?? new LinkStatusSettings();
                s.Version = 1;
                if (!Enum.IsDefined(typeof(FolderDisplayMode), s.FolderMode)) s.FolderMode = FolderDisplayMode.LastN;
                s.DesktopConnectorRoots ??= new List<string>();
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
