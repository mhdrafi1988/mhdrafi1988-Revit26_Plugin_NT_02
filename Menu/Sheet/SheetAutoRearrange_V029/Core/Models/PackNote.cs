namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>
    /// V029: a message the packing step hands back to its caller (the Run log, or the
    /// note under the Align &amp; Distribute options), e.g. "alignment skipped, does not fit".
    /// </summary>
    public sealed class PackNote
    {
        /// <summary>True for a warning (something was skipped), false for plain information.</summary>
        public bool IsWarning { get; }

        /// <summary>The text to show.</summary>
        public string Message { get; }

        public PackNote(bool isWarning, string message)
        {
            IsWarning = isWarning;
            Message = message;
        }
    }
}
