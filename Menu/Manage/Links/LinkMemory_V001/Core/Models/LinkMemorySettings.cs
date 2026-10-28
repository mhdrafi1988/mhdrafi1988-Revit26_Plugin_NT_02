namespace Revit26_Plugin_Revised.LinkMemory.V001.Core.Models
{
    /// <summary>
    /// Window options remembered between Revit sessions, in
    /// %AppData%\Revit26_Plugin_Revised\LinkMemory\settings_v001.json.
    /// </summary>
    public sealed class LinkMemorySettings
    {
        /// <summary>Format this build writes. Bump and migrate when the shape changes.</summary>
        public const int CurrentFormat = 1;

        /// <summary>Format of the file as read; new files get <see cref="CurrentFormat"/>.</summary>
        public int FormatVersion { get; set; } = CurrentFormat;

        /// <summary>Unload links the set had unloaded (default on, as before).</summary>
        public bool UnloadUnloaded { get; set; } = true;

        /// <summary>Reload links that are already loaded (default off, as before).</summary>
        public bool ReloadLoaded { get; set; }
    }
}
