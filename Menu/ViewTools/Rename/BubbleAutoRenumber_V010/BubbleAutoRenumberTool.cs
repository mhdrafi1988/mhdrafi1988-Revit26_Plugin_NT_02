namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010
{
    /// <summary>
    /// Name and version of this tool version, in its own folder so the tool builds
    /// and reports itself correctly with no change to Shared/Services/ToolCatalog.cs
    /// (same arrangement as WorksetCreator V015). Adding the matching ToolCatalog
    /// entry is still worth it — see BubbleAutoRenumber_V010_Wiring.md §2 — because
    /// ToolGuard reads the catalog, not this class, for window titles and error logs.
    /// </summary>
    internal static class BubbleAutoRenumberTool
    {
        public const string Name = "Bubble Auto Renumber";

        /// <summary>Semantic version, matching the ToolCatalog entry.</summary>
        public const string Version = "10.0.0";

        /// <summary>Legacy build tag carried by this folder and namespace.</summary>
        public const string BuildTag = "V010";

        /// <summary>Shown in the shell header's version slot.</summary>
        public const string ShellVersion = "V010";
    }
}
