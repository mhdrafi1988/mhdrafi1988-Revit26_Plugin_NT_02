// =======================================================
// File: IResultExporter.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
// New in V034. Saves a finished run's results to a file (Excel in Revit).
// =======================================================

using System;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
{
    /// <summary>Exports a run's results.</summary>
    public interface IResultExporter
    {
        /// <summary>Exports to the folder in the result's request. Returns the file path, or null
        /// when the export failed (the reason is logged).</summary>
        string ExportAuto(AutoSlopeResult result, Action<LogLevel, string> log);
    }
}
