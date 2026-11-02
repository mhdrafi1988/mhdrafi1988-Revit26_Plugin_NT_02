using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models
{
    /// <summary>One view's place in the sequence: what it holds now, and what it will get.</summary>
    public class RenumberAssignment
    {
        public long ViewId { get; init; }
        public string ViewName { get; init; } = string.Empty;
        public string KindLabel { get; init; } = string.Empty;

        /// <summary>Detail number before the run.</summary>
        public string PreviousNumber { get; init; } = string.Empty;

        /// <summary>
        /// Placeholder written in pass 1 so no two views hold the same number while
        /// the sequence is being rewritten — see DetailNumberAllocator for why.
        /// </summary>
        public string TempNumber { get; init; } = string.Empty;

        /// <summary>Detail number after the run.</summary>
        public string FinalNumber { get; init; } = string.Empty;

        /// <summary>True when the view already reads as FinalNumber — still written, but
        /// counted separately so the metrics don't overstate what changed.</summary>
        public bool IsUnchanged => PreviousNumber == FinalNumber;
    }

    /// <summary>A row that will not be touched, and why.</summary>
    public class RenumberSkip
    {
        public long ViewId { get; init; }
        public string ViewName { get; init; } = string.Empty;
        public string KindLabel { get; init; } = string.Empty;
        public string CurrentNumber { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    /// <summary>
    /// The whole run, decided before a single parameter is written: who gets what,
    /// who is skipped, and the display order. Also drives the window's live preview —
    /// the grid's "New #" column is this plan, so what you see is what runs.
    /// </summary>
    public class RenumberPlan
    {
        /// <summary>In sequence order: top-left to bottom-right.</summary>
        public List<RenumberAssignment> Assignments { get; } = new();

        /// <summary>In position order, interleaved with the assignments by Preview.</summary>
        public List<RenumberSkip> Skips { get; } = new();

        /// <summary>Every in-scope row in positional order, assignment or skip, for the grid.</summary>
        public List<RenumberPreviewRow> Preview { get; } = new();
    }

    /// <summary>One grid row of the preview.</summary>
    public class RenumberPreviewRow
    {
        public long ViewId { get; init; }
        public string ViewName { get; init; } = string.Empty;
        public string KindLabel { get; init; } = string.Empty;
        public string PillGroup { get; init; } = string.Empty;
        public string CurrentNumber { get; init; } = string.Empty;

        /// <summary>The number this row will get, or an em dash when it is skipped.</summary>
        public string NewNumber { get; init; } = string.Empty;

        public bool IsSkipped { get; init; }
        public string SkipReason { get; init; } = string.Empty;
    }
}
