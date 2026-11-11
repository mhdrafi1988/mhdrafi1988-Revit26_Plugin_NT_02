using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure
{
    /// <summary>
    /// Remembers which item of each Quick Align / Quick Distribute split button on the Tagging panel was used last, in
    /// <c>%AppData%\Revit26_Plugin_Revised\AlignSpotElevations\quicksplit.json</c>, so the split button
    /// shows and runs it again after Revit restarts. A missing, unreadable or newer file means "no
    /// memory": the split buttons start on their first item. Nothing here ever throws.
    /// </summary>
    public static class QuickSplitMemory
    {
        private const int CurrentVersion = 1;
        private static readonly object Gate = new object();
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        private static Dictionary<string, string> _last;

        /// <summary>Internal name of the Quick Align Vertical split button.</summary>
        public const string SplitVertical = "Split_AlignSpotElevations_QuickVertical";
        /// <summary>Internal name of the Quick Align Horizontal split button.</summary>
        public const string SplitHorizontal = "Split_AlignSpotElevations_QuickHorizontal";
        /// <summary>Internal name of the Quick Align to Tag split button.</summary>
        public const string SplitToTag = "Split_AlignSpotElevations_QuickToTag";
        /// <summary>Internal name of the Quick Distribute Vertical split button.</summary>
        public const string SplitDistributeVertical = "Split_AlignSpotElevations_QuickDistributeVertical";
        /// <summary>Internal name of the Quick Distribute Horizontal split button.</summary>
        public const string SplitDistributeHorizontal = "Split_AlignSpotElevations_QuickDistributeHorizontal";

        /// <summary>Full path of the memory file.</summary>
        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Revit26_Plugin_Revised", "AlignSpotElevations", "quicksplit.json");

        /// <summary>Internal name of the push button last used in <paramref name="splitName"/>, or null if none is remembered.</summary>
        public static string GetLast(string splitName)
        {
            lock (Gate)
            {
                EnsureLoaded();
                return _last.TryGetValue(splitName, out string button) ? button : null;
            }
        }

        /// <summary>Records <paramref name="buttonName"/> as the last item used in <paramref name="splitName"/>; failures are ignored.</summary>
        public static void Remember(string splitName, string buttonName)
        {
            try
            {
                lock (Gate)
                {
                    EnsureLoaded();
                    if (_last.TryGetValue(splitName, out string current) && current == buttonName) return;
                    _last[splitName] = buttonName;
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(new FileModel { Version = CurrentVersion, Last = _last }, Json));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlignSpotElevations] quick split memory not saved: {ex.Message}");
            }
        }

        private static void EnsureLoaded()
        {
            if (_last != null) return;
            _last = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(FilePath)) return;
                FileModel m = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(FilePath), Json);
                if (m?.Last == null || m.Version > CurrentVersion) return;
                _last = new Dictionary<string, string>(m.Last);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AlignSpotElevations] quick split memory not loaded: {ex.Message}");
            }
        }

        private sealed class FileModel
        {
            public int Version { get; set; }
            public Dictionary<string, string> Last { get; set; }
        }
    }
}
