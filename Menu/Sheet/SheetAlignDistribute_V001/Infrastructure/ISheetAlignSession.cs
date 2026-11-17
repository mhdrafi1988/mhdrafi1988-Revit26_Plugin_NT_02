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
        /// Re-reads the sheet, re-plans with <paramref name="settings"/> and moves the elements — but
        /// only while the fresh plan still has <paramref name="previewSignature"/>, i.e. still matches
        /// what the user saw. <paramref name="unticked"/> lists the element keys the user left out.
        /// Returns false when Revit did not accept the request.
        /// </summary>
        bool Apply(AlignDistributeSettings settings, IReadOnlySet<long> unticked, string previewSignature,
            Action<SessionResult> done);
    }
}
