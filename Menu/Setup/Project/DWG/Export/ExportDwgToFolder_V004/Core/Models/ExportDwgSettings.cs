namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models
{
    /// <summary>
    /// Persisted settings for Export DWG to Folder.
    /// Serialised by <see cref="Revit26_Plugin_Revised.Shared.Services.SettingsService{T}"/>.
    /// <para>
    /// Format version 2 (tool v2.0.0): the scan scope (source and view-type boxes) is no longer
    /// remembered — every window opens with nothing in scope. The v1 scope fields below are kept
    /// only so version-1 files still load; they are ignored.
    /// </para>
    /// <para>
    /// Format version 3 (V002 build): adds <see cref="DeleteAfterExportMode"/> so the post-export
    /// delete prompt can be silenced (always delete / never delete) instead of asking every run.
    /// </para>
    /// <para>
    /// Format version 4 (V004 build): adds the layer-fidelity options —
    /// <see cref="ExportSetupName"/>, <see cref="LayerPropertyMode"/>,
    /// <see cref="CopyOriginalForLinks"/> and <see cref="UseSharedCoords"/>; plus
    /// <see cref="NameTemplate"/> and <see cref="WriteManifest"/>.
    /// </para>
    /// </summary>
    public class ExportDwgSettings
    {
        /// <summary>Settings format version. 1 = tool v1.x, 2 = tool v2.0.0+, 3 = V002+, 4 = V004+.</summary>
        public int Version { get; set; } = 4;

        // ── Layer fidelity (V004) ───────────────────────────────────────────

        /// <summary>
        /// Name of a DWG/DXF export setup saved in the model, as listed by
        /// <c>ExportDWGSettings.ListNames(doc)</c>. Empty means "Revit defaults" — build the
        /// options from scratch instead of loading a setup. A setup carries the layer, linetype,
        /// pattern and font tables, which is what actually preserves the layer structure.
        /// </summary>
        public string ExportSetupName { get; set; } = string.Empty;

        /// <summary>
        /// How entity properties are written into the output — the setting that decides what the
        /// exported layer structure looks like. Defaults to
        /// <see cref="LayerPropertyMode.LayersOnly"/>, which gives a 1:1 layer set matching the
        /// source file. V002/V003 effectively exported with
        /// <see cref="LayerPropertyMode.FidelityFirst"/> (Revit's default).
        /// </summary>
        public LayerPropertyMode LayerPropertyMode { get; set; } = LayerPropertyMode.LayersOnly;

        /// <summary>
        /// For a CAD <i>link</i> whose source file resolves on disk, copy that file instead of
        /// re-exporting it through its host view. A copy is byte-for-byte identical, so the layer
        /// structure is exactly the source's. Default on. Turning it off restores V002/V003
        /// behaviour (every link re-exported from the view).
        /// </summary>
        public bool CopyOriginalForLinks { get; set; } = true;

        /// <summary>Export about the shared coordinate origin instead of the project origin.</summary>
        public bool UseSharedCoords { get; set; } = false;

        // ── Output naming and reporting (V004) ──────────────────────────────

        /// <summary>
        /// Output-name pattern. Tokens: <c>{name} {sheet} {sheetnumber} {view} {viewtype}
        /// {source} {id}</c>. The default <c>{name}</c> is V003's behaviour — the CAD type name on
        /// its own. See <c>DwgCollectorService.ApplyNameTemplate</c>.
        /// </summary>
        public string NameTemplate { get; set; } = "{name}";

        /// <summary>
        /// Write one <c>_export-manifest_*.csv</c> per run at the output root, with a row per
        /// exported DWG — name, element id, source path, destination, route, status and reason.
        /// Default on: it is what you send a consultant or use to chase failures, and it replaces
        /// reading 30 sidecar .txt files one at a time.
        /// </summary>
        public bool WriteManifest { get; set; } = true;

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
