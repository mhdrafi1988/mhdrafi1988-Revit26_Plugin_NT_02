using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Infrastructure
{
    /// <summary>What the window asks Revit to do when the user presses Move Views.</summary>
    public sealed class ApplyRequest
    {
        /// <summary>Sheet 1.</summary>
        public long Sheet1Key { get; init; }

        /// <summary>Sheet 2.</summary>
        public long Sheet2Key { get; init; }

        /// <summary>Other target sheet (only for <see cref="TargetMode.OtherSheet"/>).</summary>
        public long OtherTargetKey { get; init; }

        /// <summary>Ticked viewports.</summary>
        public IReadOnlyCollection<long> Ticked { get; init; } = Array.Empty<long>();

        /// <summary>Options as shown.</summary>
        public SheetViewsCombineSettings Settings { get; init; } = new();

        /// <summary>Number for a new target sheet.</summary>
        public string NewSheetNumber { get; init; } = string.Empty;

        /// <summary>Name for a new target sheet.</summary>
        public string NewSheetName { get; init; } = string.Empty;

        /// <summary>Title block for a new target sheet.</summary>
        public long NewTitleBlockKey { get; init; }

        /// <summary>Signature of the plan the user saw; the run is refused when the fresh plan differs.</summary>
        public string Signature { get; init; } = string.Empty;
    }

    /// <summary>Result of any request to Revit.</summary>
    public sealed class SessionResult
    {
        /// <summary>Sheets and title blocks as read now, or null.</summary>
        public ProjectCatalog Catalog { get; init; }

        /// <summary>Sheet contents read by this request, by sheet key (a key with a null value no longer exists).</summary>
        public Dictionary<long, SheetContent> Sheets { get; init; } = new();

        /// <summary>A measured title block, or null.</summary>
        public long MeasuredTitleBlockKey { get; init; }

        /// <summary>Its extent at 0,0, or null.</summary>
        public SheetRect? TitleBlockFrame { get; init; }

        /// <summary>Why it could not be measured, or null.</summary>
        public string TitleBlockProblem { get; init; }

        /// <summary>The run's outcome (Apply only), or null.</summary>
        public CombineRunResult Run { get; init; }

        /// <summary>True when the run committed.</summary>
        public bool Applied { get; init; }

        /// <summary>Severity of <see cref="Message"/>.</summary>
        public LogLevel Level { get; init; } = LogLevel.Info;

        /// <summary>Outcome line for the log, or null.</summary>
        public string Message { get; init; }

        /// <summary>More log lines.</summary>
        public List<LogEntry> Details { get; init; } = new();
    }

    /// <summary>
    /// The window's only link to Revit. Every request runs inside Revit's API context; the
    /// callback runs once the request has been handled (on Revit's UI thread).
    /// </summary>
    public interface ICombineSession : IDisposable
    {
        /// <summary>Sheets and title blocks as read when the tool opened.</summary>
        ProjectCatalog InitialCatalog { get; }

        /// <summary>The active sheet's contents when the tool opened, or null.</summary>
        SheetContent InitialActiveSheet { get; }

        /// <summary>Re-reads the sheets list and the given sheets.</summary>
        bool Refresh(IReadOnlyCollection<long> sheetKeys, Action<SessionResult> done);

        /// <summary>Reads the given sheets.</summary>
        bool LoadSheets(IReadOnlyCollection<long> sheetKeys, Action<SessionResult> done);

        /// <summary>Measures a title block for a new sheet.</summary>
        bool MeasureTitleBlock(long titleBlockKey, Action<SessionResult> done);

        /// <summary>Re-reads and re-plans; moves the views only when the plan matches the preview.</summary>
        bool Apply(ApplyRequest request, Action<SessionResult> done);

        /// <summary>Opens a sheet in Revit.</summary>
        bool OpenSheet(long sheetKey, Action<SessionResult> done);
    }
}
