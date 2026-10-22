using System;

namespace Revit26_Plugin_Revised.DwgStatistics.V005.Core.Models
{
    /// <summary>
    /// Title, version and limits of the tool. Kept local instead of in Shared ToolCatalog so this tool
    /// builds on its own; move it into ToolCatalog (like ToolCatalog.DwgManager) if you prefer one catalog.
    /// </summary>
    public static class DwgStatisticsTool
    {
        /// <summary>Window / dialog title.</summary>
        public const string Title = "DWG Statistics";

        /// <summary>Version shown in the window header.</summary>
        public const string Version = "V005";

        /// <summary>"DWG Statistics V005".</summary>
        public const string Label = Title + " " + Version;

        /// <summary>Default for "Open Ticked Views" (same as DWG Manager V003).</summary>
        public const int DefaultMaxOpenViews = 5;

        /// <summary>V004: the user can raise the open-views limit up to this.</summary>
        public const int MaxOpenViewsLimit = 20;

        /// <summary>V004: deep-scan batch length — Revit work per external-event call before the UI repaints.</summary>
        public const int ScanBatchMilliseconds = 200;

        /// <summary>V005: folder created next to the project file for the shared scan history.</summary>
        public const string SharedHistoryFolderName = "_DwgStatistics";

        /// <summary>V005: newest scans kept in the history log.</summary>
        public const int HistoryLength = 50;

        /// <summary>V004: %AppData%\Revit26_Plugin_Revised\DwgStatistics (settings + last-scan snapshots).</summary>
        public static string DataFolder =>
            global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Revit26_Plugin_Revised", "DwgStatistics");
    }
}
