namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Models
{
    /// <summary>
    /// Window options remembered between Revit sessions, in
    /// %AppData%\Revit26_Plugin_Revised\LinkMemory\settings_v002.json.
    /// </summary>
    public sealed class LinkMemorySettings
    {
        /// <summary>Format this build writes. Bump and migrate when the shape changes.</summary>
        public const int CurrentFormat = 1;

        /// <summary>Format of the file as read; new files get <see cref="CurrentFormat"/>.</summary>
        public int FormatVersion { get; set; } = CurrentFormat;

        /// <summary>Which marks to follow and which kinds of link to list (all on by default, as before).</summary>
        public CollectOptions Collect { get; set; } = new();

        /// <summary>Unload Revit / IFC links the memory doesn't use (default on, as before).</summary>
        public bool UnloadUnused { get; set; } = true;

        /// <summary>Also reload used links that are already loaded (default off, as before).</summary>
        public bool ReloadUsed { get; set; }
    }
}
