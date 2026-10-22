using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V005
{
    /// <summary>
    /// Auto-saves the run log to a fixed location on every completed Run —
    /// no folder-picker, no per-session prompt. Confirmed: always
    /// My Documents\Revit26_Plugin_Revised\SectionViewAutoTagger\Logs\.
    /// Filename: SectionViewAutoTagger_Logs_{yyyy-MM-dd}_{HH-mm}.txt
    /// </summary>
    public class LogExportHelper
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Revit26_Plugin_Revised", "SectionViewAutoTagger", "Logs");

        /// <summary>
        /// Writes all log entries to a timestamped .txt file in the fixed
        /// log folder. Returns the full path on success, or null if the
        /// write failed (failure is logged by the caller, not thrown here —
        /// a failed log export must never block or crash the Run).
        /// </summary>
        public string SaveLog(IEnumerable<LogEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(LogDir);

                string fileName = $"SectionViewAutoTagger_Logs_{DateTime.Now:yyyy-MM-dd}_{DateTime.Now:HH-mm}.txt";
                string fullPath = Path.Combine(LogDir, fileName);

                var lines = entries.Select(e => e.ToString());
                File.WriteAllLines(fullPath, lines);

                return fullPath;
            }
            catch
            {
                return null;
            }
        }
    }
}
