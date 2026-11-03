using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Services
{
    /// <summary>Writes the log to {ToolName}_Logs_{yyyy-MM-dd}_{HH-mm}.txt. Pure .NET.</summary>
    public static class FloorViewFocusLogExporter
    {
        public static string BuildFileName(DateTime now)
            => $"FloorViewFocus_Logs_{now:yyyy-MM-dd}_{now:HH-mm}.txt";

        /// <returns>Full path of the written file.</returns>
        public static string Write(string folder, IEnumerable<LogEntry> entries)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, BuildFileName(DateTime.Now));
            File.WriteAllLines(path, entries.Select(e => e.ToString()), Encoding.UTF8);
            return path;
        }
    }
}
