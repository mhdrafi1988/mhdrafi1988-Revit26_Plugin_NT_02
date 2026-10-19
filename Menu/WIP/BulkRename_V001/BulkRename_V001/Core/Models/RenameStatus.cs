namespace Revit26_Plugin.BulkRename.V001.Core.Models
{
    /// <summary>State of one row in the Bulk Rename grid.</summary>
    public enum RenameStatus
    {
        /// <summary>The new name equals the current name, or the row is not ticked.</summary>
        Unchanged,

        /// <summary>Ticked, changed and valid: it will be renamed on Apply.</summary>
        Ready,

        /// <summary>The new name is empty or contains a character Revit does not allow.</summary>
        Invalid,

        /// <summary>Another item would end up with the same name.</summary>
        Duplicate,

        /// <summary>Cannot be renamed (built-in, or owned by another user).</summary>
        Locked
    }
}
