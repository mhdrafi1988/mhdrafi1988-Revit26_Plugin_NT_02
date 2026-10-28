namespace Revit26_Plugin_Revised.WorksetMemory.V001.Core.Models
{
    /// <summary>
    /// Window options remembered between Revit sessions, in
    /// %AppData%\Revit26_Plugin_Revised\WorksetMemory\settings.json.
    /// </summary>
    public sealed class WorksetMemorySettings
    {
        /// <summary>Settings file name in the tool folder.</summary>
        public const string FileName = "settings.json";

        /// <summary>Format this build writes. Bump and migrate when the shape changes.</summary>
        public const int CurrentFormat = 1;

        /// <summary>Format of the file as read; new files get <see cref="CurrentFormat"/>.</summary>
        public int FormatVersion { get; set; } = CurrentFormat;

        /// <summary>Make the set's active workset current after reload (default on, as before).</summary>
        public bool RestoreActiveWorkset { get; set; } = true;
    }
}
