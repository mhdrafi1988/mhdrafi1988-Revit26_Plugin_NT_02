using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V028.Infrastructure.Helpers
{
    /// <summary>
    /// Writes the log panel to a .txt file.
    ///
    /// V008 CHANGE: removed the folder-picker dialog entirely per explicit
    /// request ("ONE DIALOGUE IS OPENING TO SAVE SOMETHING, REMOVE IT. JUST
    /// KEEP THE FILE IN A DEFAULT LOCATION"). Logs now always save silently
    /// to the suite-standard default location:
    ///   My Documents\Revit26_Plugin_Revised\SheetAutoRearrange\Logs\
    /// matching the convention used elsewhere in the suite
    /// (%AppData%\Revit26_Plugin_Revised\[ToolName]\settings.json for settings,
    /// My Documents\Revit26_Plugin_Revised\[ToolName]\Logs\ for logs). The folder
    /// is created automatically if it doesn't exist. No dialog, no per-
    /// session prompt — both auto-save-on-completion and manual Export Log
    /// now write here directly.
    /// </summary>
    public static class LogExportHelper
    {
        public static string BuildFileName()
        {
            var now = DateTime.Now;
            return $"SheetAutoRearrange_Logs_{now:yyyy-MM-dd}_{now:HH-mm}.txt";
        }

        /// <summary>The fixed default folder logs are saved to. Created on first use if it doesn't exist.</summary>
        public static string GetDefaultLogFolder()
        {
            string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(myDocs, "Revit26_Plugin_Revised", "SheetAutoRearrange", "Logs");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Writes the log directly to the fixed default folder using the standard filename. Returns the full path written, for logging/display.</summary>
        public static string SaveToDefaultFolder(ObservableCollection<LogEntry> log)
        {
            string folder = GetDefaultLogFolder();
            string fullPath = Path.Combine(folder, BuildFileName());
            File.WriteAllText(fullPath, BuildLogText(log), Encoding.UTF8);
            return fullPath;
        }

        private static string BuildLogText(ObservableCollection<LogEntry> log)
            => string.Join(Environment.NewLine, log.Select(l => l.ToString()));
    }
}
