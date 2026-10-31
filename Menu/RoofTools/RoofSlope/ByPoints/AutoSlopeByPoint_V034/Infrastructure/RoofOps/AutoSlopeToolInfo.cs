// =======================================================
// File: AutoSlopeToolInfo.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps
// New in V034. The tool version, read from ToolCatalog. In V033 this was
// AutoSlopeRunner.ToolVersion; the runner moved to the Application layer
// and now receives the version through its constructor.
// =======================================================

using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps
{
    /// <summary>Version information for this tool.</summary>
    internal static class AutoSlopeToolInfo
    {
        /// <summary>This tool's version from ToolCatalog, or the fallback if it is not listed.</summary>
        public static string ToolVersion =>
            ToolCatalog.Find(typeof(AutoSlopeToolInfo))?.Version ?? AutoSlopeConstants.FallbackToolVersion;
    }
}
