namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models
{
    /// <summary>
    /// What happens to one successfully-exported CAD instance in the model, chosen per row in the
    /// post-export review window. NEW in V004 — V002/V003 offered only "delete all / delete the
    /// links / delete the imports" through a Revit <c>TaskDialog</c>.
    /// </summary>
    public enum PostExportAction
    {
        /// <summary>Leave the instance in the model untouched. Default.</summary>
        Keep,

        /// <summary>Delete the instance from the model.</summary>
        DeleteFromModel,

        /// <summary>
        /// Replace a CAD <i>link</i> with an import of the same file at the same placement.
        /// Only valid for a link whose source file resolves on disk — Revit has no API for
        /// Manage Links' "Import" button, so this deletes the link and re-imports it.
        /// </summary>
        ConvertToImport,

        /// <summary>
        /// Replace the instance with a CAD <i>link</i> to the DWG this tool just exported for it.
        /// The other direction, and the easier one: that file was written from this very instance,
        /// so it already carries the right position. For an import this shrinks the model and leaves
        /// the CAD editable outside Revit.
        /// </summary>
        ReplaceWithLinkToExported
    }
}
