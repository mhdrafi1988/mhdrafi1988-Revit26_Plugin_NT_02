using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    public sealed class CheckerSettings
    {
        public string LastHostPath { get; set; } = "";
        public string LastFolder { get; set; } = "";
        public List<string> RecentHosts { get; set; } = new List<string>();
        public bool IncludeNested { get; set; } = true;
        public bool IncludeSubfolders { get; set; } = true;
        public bool FilterProblems { get; set; }
        public string TypeFilter { get; set; } = "All";

        // ---- V004: Source filter ----
        public string SourceFilter { get; set; } = "All";
        /// <summary>Ticked by default: Source and Type lists show only the values present in the scan.</summary>
        public bool AvailableTypesOnly { get; set; } = true;
        /// <summary>Folder roots that mean "ACC content synced by Desktop Connector".</summary>
        public List<string> DesktopConnectorRoots { get; set; } = new List<string>
        {
            @"%USERPROFILE%\ACCDocs", @"%USERPROFILE%\DC", @"%USERPROFILE%\Autodesk Docs",
            @"%LOCALAPPDATA%\Autodesk\Autodesk Desktop Connector",
        };
        /// <summary>Folder roots that mean "a cloud-sync folder" (add Dropbox / My Drive here if you want them flagged).</summary>
        public List<string> CloudSyncRoots { get; set; } = new List<string>
        {
            @"%ONEDRIVE%", @"%USERPROFILE%\OneDrive", @"%USERPROFILE%\OneDrive - ",
        };
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 800;
        public bool WindowMaximized { get; set; }
        public string LastExportFolder { get; set; } = "";

        public void PushRecentHost(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            RecentHosts.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            RecentHosts.Insert(0, path);
            if (RecentHosts.Count > 10) RecentHosts.RemoveRange(10, RecentHosts.Count - 10);
        }
    }

    /// <summary>Remembers the user's choices in %AppData%\Revit26_Plugin\LinkVersionChecker\settings.json.</summary>
    public static class SettingsStore
    {
        private static string FilePath => Path.Combine(LoadTimeStore.Folder, "settings.json");

        public static CheckerSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<CheckerSettings>(File.ReadAllText(FilePath)) ?? new CheckerSettings();
            }
            catch { /* ignore */ }
            return new CheckerSettings();
        }

        public static void Save(CheckerSettings s)
        {
            try
            {
                Directory.CreateDirectory(LoadTimeStore.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }
    }
}
