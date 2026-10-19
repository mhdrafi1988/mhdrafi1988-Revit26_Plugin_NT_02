namespace Revit26_Plugin.ExportDwgToFolder.V003.Core.Models
{
    /// <summary>
    /// Persisted settings for Export DWG to Folder.
    /// Serialised by <see cref="Revit26_Plugin.Shared.Services.SettingsService{T}"/>.
    /// <para>
    /// Format version 2 (tool v2.0.0): the scan scope (source and view-type boxes) is no longer
    /// remembered — every window opens with nothing in scope. The v1 scope fields below are kept
    /// only so version-1 files still load; they are ignored.
    /// </para>
    /// <para>
    /// Format version 3 (V002 build): adds <see cref="DeleteAfterExportMode"/> so the post-export
    /// delete prompt can be silenced (always delete / never delete) instead of asking every run.
    /// </para>
    /// </summary>
    public class ExportDwgSettings
    {
        /// <summary>Settings format version. 1 = tool v1.x, 2 = tool v2.0.0+, 3 = V002+.</summary>
        public int Version { get; set; } = 3;

        /// <summary>Last output folder used.</summary>
        public string LastOutputFolder { get; set; } = string.Empty;

        /// <summary>Put each DWG in a plan / section / drafting / no_views subfolder.</summary>
        public bool SubfoldersByViewType { get; set; } = true;

        /// <summary>Write a .txt with view, sheet and element info next to each DWG.</summary>
        public bool GenerateSidecar { get; set; } = true;

        /// <summary>Overwrite existing files instead of skipping them.</summary>
        public bool Overwrite { get; set; } = false;

        /// <summary>Whether (and how) to offer deleting successfully-exported rows from the model.</summary>
        public DeleteAfterExportMode DeleteAfterExportMode { get; set; } = DeleteAfterExportMode.Ask;

        // ── Format v1 fields — kept for compatibility, ignored since v2 ──────

        /// <summary>v1 only (ignored): linked CAD in scope.</summary>
        public bool ExportLinked { get; set; } = true;

        /// <summary>v1 only (ignored): imported CAD in scope.</summary>
        public bool ExportImported { get; set; } = true;

        /// <summary>v1 only (ignored): plan views in scope.</summary>
        public bool IncludePlan { get; set; } = true;

        /// <summary>v1 only (ignored): section views in scope.</summary>
        public bool IncludeSection { get; set; } = true;

        /// <summary>v1 only (ignored): drafting views in scope.</summary>
        public bool IncludeDrafting { get; set; } = true;

        /// <summary>v1 only (ignored): DWGs with no view in scope.</summary>
        public bool IncludeNoViews { get; set; } = true;
    }
}
