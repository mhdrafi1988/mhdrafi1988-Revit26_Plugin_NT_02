namespace Revit26_Plugin_Revised.FloorViewFocus.V001.Core.Models
{
    /// <summary>Tool-level constants. Pure .NET — no Revit API.</summary>
    public static class FloorViewFocusDefaults
    {
        public const string ToolName = "FloorViewFocus";
        public const string Version = "V004";
        public const string Title = "Floor View Focus — V004";

        /// <summary>Default value of both margin input fields (mm).</summary>
        public const double DefaultMarginMm = 20.0;

        /// <summary>Fixed offset (mm) added on top of the user-entered margin. Not editable in UI.</summary>
        public const double DefaultOffsetMm = 20.0;
    }
}
