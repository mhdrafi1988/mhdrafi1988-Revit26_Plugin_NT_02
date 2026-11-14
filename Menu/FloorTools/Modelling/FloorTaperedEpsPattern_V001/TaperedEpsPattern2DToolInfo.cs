using System;
using System.IO;
using System.Reflection;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001
{
    /// <summary>Tool identity shown in the window header and the ribbon (see TOOL_VERSIONING.md).</summary>
    public static class TaperedEpsPattern2DToolInfo
    {
        public const string Title = "Tapered EPS 2D Pattern";
        public const string Version = "V006";
        public const string RevitVersion = "Revit 2026";
        public const string CommandClass = "Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Commands.TaperedEpsPattern2DCommand";

        /// <summary>Last write time of the plugin assembly — a practical build stamp.</summary>
        public static string BuildDate
        {
            get
            {
                try
                {
                    string path = Assembly.GetExecutingAssembly().Location;
                    return File.GetLastWriteTime(path).ToString("yyyy-MM-dd");
                }
                catch (Exception)
                {
                    return "unknown";
                }
            }
        }
    }
}
