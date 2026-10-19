namespace Revit26_Plugin.ExportDwgToFolder.V003.Core.Models
{
    /// <summary>
    /// How the post-export "delete from the model?" prompt behaves. Persisted across sessions
    /// alongside the other export options.
    /// </summary>
    public enum DeleteAfterExportMode
    {
        /// <summary>Ask every time a run exports at least one row successfully (default).</summary>
        Ask,

        /// <summary>Delete every successfully-exported row without asking.</summary>
        Always,

        /// <summary>Never offer to delete; successfully-exported rows are always left in the model.</summary>
        Never
    }
}
