// =======================================================
// File: ExcelResultExporter.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
// New in V034. IResultExporter that writes the same workbook as V033
// (ExcelExportService.ExportAuto).
// =======================================================

using System;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
{
    /// <summary>Exports run results to Excel.</summary>
    internal sealed class ExcelResultExporter : IResultExporter
    {
        /// <inheritdoc/>
        public string ExportAuto(AutoSlopeResult result, Action<LogLevel, string> log) =>
            ExcelExportService.ExportAuto(result, log);
    }
}
