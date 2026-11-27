using System;
using System.IO;
using System.Reflection;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001
{
    /// <summary>Tool identity shown in the window header and the ribbon.</summary>
    public static class EpsSteppedToolInfo
    {
        public const string Title = "EPS Stepped Insulation Layout";
        public const string Version = "V001";
        public const string RevitVersion = "Revit 2026";
        public const string CommandClass = "Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Commands.EpsSteppedInsulationCommand";

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
