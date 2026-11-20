using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Infrastructure
{
    /// <summary>What the window asks Revit to create.</summary>
    public sealed class ApplyRequest
    {
        /// <summary>Keys of the ticked sections.</summary>
        public IReadOnlyCollection<long> TickedKeys { get; init; } = Array.Empty<long>();

        /// <summary>More sections to keep in the snapshot read afterwards (picked ones).</summary>
        public IReadOnlyCollection<long> KeepKeys { get; init; } = Array.Empty<long>();

        /// <summary>Layout options as shown.</summary>
        public SectionsToSheetSettings Settings { get; init; } = new();

        /// <summary>Number for the first new sheet.</summary>
        public string FirstSheetNumber { get; init; } = string.Empty;

        /// <summary>Name for the new sheets.</summary>
        public string SheetName { get; init; } = string.Empty;

        /// <summary>Title block type.</summary>
        public long TitleBlockKey { get; init; }

        /// <summary>Viewport type (0 = Revit's default).</summary>
        public long ViewportTypeKey { get; init; }

        /// <summary>Open the first new sheet when done.</summary>
        public bool OpenSheet { get; init; }

        /// <summary>The existing sheet to place on, when the settings target an existing sheet (0 = none chosen).</summary>
        public long TargetSheetKey { get; init; }

        /// <summary>Signature of the plan the user saw; the run is refused when the fresh plan differs.</summary>
        public string Signature { get; init; } = string.Empty;
    }

    /// <summary>Result of any request to Revit.</summary>
    public sealed class SessionResult
    {
        /// <summary>The model as read now, or null when it was not re-read.</summary>
        public SourceSnapshot Snapshot { get; init; }

        /// <summary>True when the target sheet was read (then <see cref="Target"/> or <see cref="TargetProblem"/> is set).</summary>
        public bool TargetRead { get; init; }

        /// <summary>The existing target sheet as read now, or null.</summary>
        public TargetSheetContent Target { get; init; }

        /// <summary>Why <see cref="Target"/> is null after a read.</summary>
        public string TargetProblem { get; init; }

        /// <summary>A new measurement, or null.</summary>
        public MeasureResult Measure { get; init; }

        /// <summary>Sections picked in the view (Pick only), or null.</summary>
        public IReadOnlyList<long> PickedKeys { get; init; }

        /// <summary>True when the run changed the model (sheets created, or views placed on the target).</summary>
        public bool Applied { get; init; }

        /// <summary>Number of the last sheet created (when <see cref="Applied"/>), or null when none was.</summary>
        public string LastSheetNumber { get; init; }

        /// <summary>Severity of <see cref="Message"/>.</summary>
        public LogLevel Level { get; init; } = LogLevel.Info;

        /// <summary>Outcome line for the log, or null.</summary>
        public string Message { get; init; }

        /// <summary>More log lines (what moved, what was skipped …).</summary>
        public List<LogEntry> Details { get; init; } = new();
    }

    /// <summary>
    /// The window's only link to Revit. Every request runs inside Revit's API context; the
    /// callback runs once the request has been handled (on Revit's UI thread).
    /// </summary>
    public interface ISectionsSession : IDisposable
    {
        /// <summary>The model as read when the tool opened.</summary>
        SourceSnapshot Initial { get; }

        /// <summary>Measurement made when the tool opened, for <see cref="InitialTitleBlockKey"/> / <see cref="InitialViewportTypeKey"/>.</summary>
        MeasureResult InitialMeasure { get; }

        /// <summary>Title block chosen from the settings when the tool opened (0 when there is none).</summary>
        long InitialTitleBlockKey { get; }

        /// <summary>Viewport type chosen from the settings when the tool opened (0 = Revit's default).</summary>
        long InitialViewportTypeKey { get; }

        /// <summary>The existing sheet saved as the target, as read when the tool opened, or null.</summary>
        TargetSheetContent InitialTarget { get; }

        /// <summary>
        /// Re-reads the model and the Revit selection, keeping <paramref name="keepKeys"/> in the
        /// snapshot, and the target sheet <paramref name="targetSheetKey"/> (0 = none).
        /// </summary>
        bool Refresh(IReadOnlyCollection<long> keepKeys, long targetSheetKey, Action<SessionResult> done);

        /// <summary>Reads what is on the existing sheet <paramref name="sheetKey"/>.</summary>
        bool ReadTarget(long sheetKey, Action<SessionResult> done);

        /// <summary>Lets the user pick section markers in the source view.</summary>
        bool Pick(IReadOnlyCollection<long> keepKeys, Action<SessionResult> done);

        /// <summary>Measures <paramref name="keys"/> with the given title block and viewport type.</summary>
        bool Measure(long titleBlockKey, long viewportTypeKey, IReadOnlyCollection<long> keys, Action<SessionResult> done);

        /// <summary>Re-reads, re-measures and re-plans; changes the model only when the plan matches the preview.</summary>
        bool Apply(ApplyRequest request, Action<SessionResult> done);
    }
}
