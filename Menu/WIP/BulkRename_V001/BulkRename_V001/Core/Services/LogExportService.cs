using Revit26_Plugin.Shared.Models;
using System.Collections;
using System.Collections.Generic;
using System.Windows;

namespace Revit26_Plugin.BulkRename.V001
{
    /// <summary>
    /// Bulk Rename's log export entry point. Lives in the tool's own namespace so the tool's
    /// <c>LogExportService.AutoSave</c> / <c>ExportWithPicker</c> calls resolve here; the
    /// work is done by <see cref="Revit26_Plugin.Shared.Services.LogExportService"/>.
    /// </summary>
    public static class LogExportService
    {
        /// <summary>Silently saves the log under %AppData%\Revit26_Plugin\Logs. Never throws; returns the path or null.</summary>
        public static string AutoSave(IEnumerable<LogEntry> log, string toolName)
            => Shared.Services.LogExportService.AutoSave(log, toolName);

        /// <summary>Asks for a folder and writes the log there. Returns the path or null.</summary>
        public static string ExportWithPicker(IEnumerable items, Window owner, string toolName)
            => Shared.Services.LogExportService.ExportWithPicker(items, owner, toolName);
    }
}
