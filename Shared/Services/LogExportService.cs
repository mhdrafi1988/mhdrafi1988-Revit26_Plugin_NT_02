using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>Writes a tool's log panel to a .txt file at a caller-chosen folder.</summary>
    public static class LogExportService
    {
        /// <summary>Writes the log to {folder}\{toolName}_Logs_{yyyy-MM-dd}_{HH-mm}.txt. Returns the full path written.</summary>
        public static string Export(string toolName, ObservableCollection<LogEntry> log, string folder)
        {
            Directory.CreateDirectory(folder);
            var now = DateTime.Now;
            string fileName = $"{toolName}_Logs_{now:yyyy-MM-dd}_{now:HH-mm}.txt";
            string fullPath = Path.Combine(folder, fileName);
            string text = string.Join(Environment.NewLine, log.Select(l => l.ToString()));
            File.WriteAllText(fullPath, text, Encoding.UTF8);
            return fullPath;
        }

        /// <summary>
        /// Silently writes the log to %AppData%\Revit26_Plugin_Revised\Logs\{toolName}. Never throws.
        /// Returns the written path, or null when the log is empty or the write failed.
        /// </summary>
        public static string AutoSave(IEnumerable<LogEntry> log, string toolName)
        {
            try
            {
                if (log == null || !log.Any()) return null;
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Revit26_Plugin_Revised", "Logs", toolName);
                return Export(toolName, new ObservableCollection<LogEntry>(log), folder);
            }
            catch (Exception) { return null; } // auto-save is best effort; must never break the tool
        }

        /// <summary>
        /// Asks the user for a folder, then writes the log there. Returns the written path,
        /// or null when cancelled, empty or the write failed (failure is shown to the user).
        /// </summary>
        public static string ExportWithPicker(IEnumerable items, Window owner, string toolName)
        {
            var entries = new ObservableCollection<LogEntry>(
                (items ?? new object[0]).Cast<object>().OfType<LogEntry>());
            if (entries.Count == 0) return null;
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder for the log file" };
                if (dlg.ShowDialog(owner) != true) return null;
                return Export(toolName, entries, dlg.FolderName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Could not export the log: " + ex.Message, toolName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }
    }
}
