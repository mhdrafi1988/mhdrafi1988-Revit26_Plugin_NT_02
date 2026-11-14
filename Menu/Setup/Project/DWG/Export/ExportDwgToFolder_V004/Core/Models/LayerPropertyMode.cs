namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models
{
    /// <summary>
    /// How entity properties (colour, linetype, lineweight) are written into the exported DWG —
    /// the setting that decides what the output's layer structure actually looks like.
    /// NEW in V004. Maps onto Revit's <c>PropOverrideMode</c> in
    /// <c>DwgExportOptionsFactory</c>; it is a tool-local enum so the settings file stays plain
    /// JSON with no Revit types in it.
    /// <para>
    /// V002/V003 left Revit's default of <see cref="FidelityFirst"/> in place, which is why an
    /// exported CAD import came out with properties stamped on every entity instead of on layers.
    /// </para>
    /// </summary>
    public enum LayerPropertyMode
    {
        /// <summary>
        /// Everything BYLAYER, no overrides (Revit's <c>PropOverrideMode.ByLayer</c>). The exported
        /// file gets the fewest layers — one per source layer, names matching 1:1 — and any Revit
        /// view override on the CAD is dropped. This is the default, because a 1:1 layer structure
        /// is normally the point of exporting the CAD back out.
        /// </summary>
        LayersOnly,

        /// <summary>
        /// Everything BYLAYER, and each override becomes a <b>new</b> layer (Revit's
        /// <c>PropOverrideMode.NewLayer</c>). Keeps what the view actually looks like and keeps
        /// by-layer control, at the cost of extra layers that were not in the source file.
        /// </summary>
        LayersPlusOverrideLayers,

        /// <summary>
        /// Category properties BYLAYER but overrides stamped BYENTITY (Revit's
        /// <c>PropOverrideMode.ByEntity</c>) — Revit's own default, and what V002/V003 exported
        /// with. Highest visual fidelity, but overridden entities stop following their layer.
        /// </summary>
        FidelityFirst
    }
}
