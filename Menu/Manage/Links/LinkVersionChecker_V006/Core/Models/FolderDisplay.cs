using System;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models
{
    /// <summary>How the "Folder in ACC" column shortens a folder path.</summary>
    public enum FolderDisplayMode
    {
        /// <summary>Show the last N folder names: <c>…\Building A\WIP</c>.</summary>
        LastN = 0,
        /// <summary>Show the first N folder names: <c>02_ARC\Models\…</c>.</summary>
        FirstN = 1,
        /// <summary>Show the whole folder below "Project Files".</summary>
        Full = 2
    }

    /// <summary>Shortens a folder path for display. The full folder is always kept on the row.</summary>
    public static class FolderDisplay
    {
        /// <summary>Smallest and largest N the window offers.</summary>
        public const int MinDepth = 1, MaxDepth = 9;

        /// <summary>
        /// Shortens <paramref name="folder"/> (a path relative to "Project Files", or "(project root)")
        /// to <paramref name="depth"/> folder names. A folder with no more than N names is returned whole.
        /// </summary>
        public static string Trim(string folder, FolderDisplayMode mode, int depth)
        {
            if (string.IsNullOrEmpty(folder) || mode == FolderDisplayMode.Full || folder.StartsWith("(")) return folder ?? "";

            int n = Math.Clamp(depth, MinDepth, MaxDepth);
            string[] parts = folder.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= n) return folder;

            return mode == FolderDisplayMode.LastN
                ? "…\\" + string.Join("\\", parts, parts.Length - n, n)
                : string.Join("\\", parts, 0, n) + "\\…";
        }
    }
}
