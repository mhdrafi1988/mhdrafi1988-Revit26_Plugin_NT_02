using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Infrastructure
{
    /// <summary>What a run did.</summary>
    public sealed class RunOutcome
    {
        /// <summary>Run ID; null when the run never started.</summary>
        public string RunId { get; init; }
        /// <summary>New roofs.</summary>
        public int Created { get; init; }
        /// <summary>Roofs that replaced one from an earlier run.</summary>
        public int Replaced { get; init; }
        /// <summary>Ticked rooms that got no roof.</summary>
        public int Skipped { get; init; }
        /// <summary>Everything the user should know.</summary>
        public IReadOnlyList<Issue> Issues { get; init; } = Array.Empty<Issue>();
        /// <summary>Saved report; null when it was not saved.</summary>
        public string ReportPath { get; init; }
        /// <summary>Why the report was not saved; null when it was, or when auto-save is off.</summary>
        public string ReportError { get; init; }
        /// <summary>The report data, for "Export now".</summary>
        public RunReport Report { get; init; }
        /// <summary>The user cancelled; nothing was changed.</summary>
        public bool Cancelled { get; init; }
        /// <summary>The run could not start, and why; null otherwise.</summary>
        public string Error { get; init; }
        /// <summary>The model read again after the run; null when cancelled or failed.</summary>
        public ModelSnapshot Snapshot { get; init; }
    }

    /// <summary>The tool's access to Revit. Every call is queued and answered on Revit's thread.</summary>
    public interface IWetAreaSession : IDisposable
    {
        /// <summary>The model as read when the tool opened.</summary>
        ModelSnapshot Initial { get; }

        /// <summary>Reads the model again. <paramref name="done"/> gets the snapshot, or null and an error text.</summary>
        void Refresh(WetAreaSettings settings, Action<ModelSnapshot, string> done);

        /// <summary>Creates roofs for the rooms with these keys.</summary>
        void Run(IReadOnlyCollection<string> roomKeys, WetAreaSettings settings, Action<RunOutcome> done);

        /// <summary>Lists earlier runs that still have elements in the model.</summary>
        void ListRuns(Action<IReadOnlyList<RunInfo>, string> done);

        /// <summary>Deletes what run <paramref name="runId"/> created. <paramref name="done"/> gets deleted, skipped (owned) and an error text.</summary>
        void RemoveRun(string runId, Action<int, int, string> done);
    }
}
