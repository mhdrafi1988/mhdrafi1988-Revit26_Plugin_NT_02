namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Models
{
    /// <summary>State of one row in the Bulk Delete grid.</summary>
    public enum DeleteStatus
    {
        /// <summary>Not ticked: stays in the model.</summary>
        Keep,

        /// <summary>Ticked: will be deleted on Delete.</summary>
        Delete,

        /// <summary>Cannot be deleted from this window (built-in, in use, or owned by another user).</summary>
        Locked
    }
}
