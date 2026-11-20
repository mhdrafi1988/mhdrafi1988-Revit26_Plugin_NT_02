using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Infrastructure
{
    /// <summary>Result of a Refresh or Apply request.</summary>
    public sealed class SessionResult
    {
        /// <summary>The sheet as it is now in the model, or null when it could not be read.</summary>
        public SheetSnapshot Snapshot { get; init; }

        /// <summary>Severity of <see cref="Message"/>.</summary>
        public LogLevel Level { get; init; } = LogLevel.Info;

        /// <summary>Outcome line for the log, or null when there is nothing to report.</summary>
        public string Message { get; init; }

        /// <summary>Elements actually moved, for the result dialog.</summary>
        public int Moved { get; init; }

        /// <summary>True when an Apply committed.</summary>
        public bool Applied { get; init; }
    }

    /// <summary>
    /// The window's only link to Revit: reads the open sheet and applies alignments inside Revit's
    /// API context. Callbacks run on the UI thread once the request has been handled.
    /// </summary>
    public interface ISheetAlignSession : IDisposable
    {
        /// <summary>The sheet as read when the tool opened.</summary>
        SheetSnapshot Initial { get; }

        /// <summary>Re-reads the sheet. Returns false when Revit did not accept the request.</summary>
        bool Refresh(FootprintMode footprintMode, Action<SessionResult> done);

        /// <summary>
        /// Moves the elements exactly as <paramref name="plan"/> (the plan the user previewed) says.
        /// Before moving, the sheet is re-read with <paramref name="footprintMode"/> and every element
        /// to be moved must still be where the preview found it; otherwise nothing moves. After the
        /// commit the sheet is read again to confirm the elements really moved.
        /// Returns false when Revit did not accept the request.
        /// </summary>
        bool Apply(AlignPlan plan, FootprintMode footprintMode, Action<SessionResult> done);
    }
}
