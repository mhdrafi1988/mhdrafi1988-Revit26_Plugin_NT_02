using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models
{
    /// <summary>
    /// One row of the post-export review: what the user chose for a successfully-exported CAD
    /// instance, and what actually happened when it was applied. NEW in V004.
    /// <para>
    /// The ViewModel fills the request side, <c>DwgExportEventHandler.ApplyPostExport</c> fills the
    /// result side, and the ViewModel maps results back onto grid rows by
    /// <see cref="ElementIdValue"/>.
    /// </para>
    /// </summary>
    public class PostExportDecision
    {
        // ── Request ─────────────────────────────────────────────────────────

        /// <summary>The exported instance this decision is about.</summary>
        public ElementId ElementId { get; set; }

        /// <summary>
        /// <see cref="ElementId"/>.Value, kept separately because the element is gone by the time
        /// results are read back and the row lookup still needs a stable key.
        /// </summary>
        public long ElementIdValue { get; set; }

        /// <summary>Display name of the instance, for log lines written handler-side.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What to do with it.</summary>
        public PostExportAction Action { get; set; } = PostExportAction.Keep;

        /// <summary>Link vs import — decides whether <see cref="PostExportAction.ConvertToImport"/> is legal.</summary>
        public DwgSourceType SourceType { get; set; }

        /// <summary>Absolute path of the link's source file; null for an import or an unresolved link.</summary>
        public string LinkedSourcePath { get; set; }

        /// <summary>Host view the instance was found in; <c>InvalidElementId</c> for model space.</summary>
        public ElementId OwnerViewId { get; set; }

        /// <summary>Full path of the DWG this instance was exported to, for the Open action.</summary>
        public string ExportedFilePath { get; set; } = string.Empty;

        /// <summary>True when the user ticked this file to be opened after applying.</summary>
        public bool OpenAfter { get; set; }

        // ── Result ──────────────────────────────────────────────────────────

        /// <summary>True when the action was carried out.</summary>
        public bool Succeeded { get; set; }

        /// <summary>
        /// What happened, in one line — the failure reason, or for a conversion what could not be
        /// carried over. Shown in the review grid's Result column and written to the log.
        /// </summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>
        /// For a completed conversion, the id of the new <c>ImportInstance</c>. The id changes, so
        /// anything else keyed to the old one needs to know.
        /// </summary>
        public long NewElementIdValue { get; set; }
    }
}
