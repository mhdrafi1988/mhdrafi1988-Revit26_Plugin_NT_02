using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Services
{
    /// <summary>
    /// Handles log file writes: silent auto-save on run completion, and manual
    /// Export (.txt) triggered by the user. Auto-save always goes to
    /// My Documents\Revit26_Plugin_Revised\LinkedDetailLineGenerator\Logs\ (no version segment,
    /// no folder picker). Manual export asks for a save folder once per session and
    /// reuses it (folder path is session-only; not persisted across sessions in Phase 1 —
    /// confirm if this should also be remembered in settings.json).
    /// </summary>
    public class LogExportService
    {
        private static readonly string AutoSaveDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "Revit26_Plugin_Revised", "LinkedDetailLineGenerator", "Logs");

        private const string ToolName = "LinkedDetailLineGenerator";

        /// <summary>Builds the standard filename: {ToolName}_Logs_{yyyy-MM-dd}_{HH-mm}.txt</summary>
        public static string BuildFileName(DateTime timestamp)
            => $"{ToolName}_Logs_{timestamp:yyyy-MM-dd}_{timestamp:HH-mm}.txt";

        /// <summary>Silent auto-save on completion. Failures are swallowed — logging
        /// must never interrupt or fail the main operation.</summary>
        public bool AutoSave(IEnumerable<LogEntry> entries, out string? savedPath)
        {
            savedPath = null;
            try
            {
                Directory.CreateDirectory(AutoSaveDir);
                string path = Path.Combine(AutoSaveDir, BuildFileName(DateTime.Now));
                File.WriteAllLines(path, entries.Select(e => e.ToString()));
                savedPath = path;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Manual export to a specific folder chosen by the user (via
        /// Microsoft.Win32.OpenFolderDialog in the View/ViewModel layer).</summary>
        public bool ExportTo(IEnumerable<LogEntry> entries, string folderPath, out string? savedPath)
        {
            savedPath = null;
            try
            {
                Directory.CreateDirectory(folderPath);
                string path = Path.Combine(folderPath, BuildFileName(DateTime.Now));
                File.WriteAllLines(path, entries.Select(e => e.ToString()));
                savedPath = path;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
