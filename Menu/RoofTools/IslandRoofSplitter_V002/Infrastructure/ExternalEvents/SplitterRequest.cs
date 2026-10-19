// IslandRoofSplitter V002 — one unit of work for the ExternalEvent handler, built by the ViewModel.
// Callbacks run on Revit's API thread; the ViewModel marshals them to its Dispatcher.
// Every request kind reports failure the same way: Error(message). Cancellation is reported as an Error too.
#nullable enable
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.ExternalEvents
{
    public enum SplitterRequestKind { Commit, Highlight, RepickRoof, RepickLines }

    public sealed class SplitterRequest
    {
        public SplitterRequestKind Kind { get; init; }

        /// <summary>Commit only.</summary>
        public CommitRequest? Commit { get; init; }

        /// <summary>Highlight only: ElementId values to select and zoom to.</summary>
        public List<long> HighlightIds { get; init; } = new();

        /// <summary>Re-pick only: the ids of the half that is NOT being re-picked.</summary>
        public long CurrentRoofId { get; init; }
        public List<long> CurrentLineIds { get; init; } = new();

        // ---- callbacks (Revit API thread) ----
        public Action<IslandCommitResult, int, int>? IslandDone { get; init; }
        public Action<List<IslandCommitResult>, bool>? CommitFinished { get; init; }
        public Action<SplitterContext>? RepickFinished { get; init; }

        /// <summary>Any failure or cancellation, for every kind. The ViewModel decides what it means per kind.</summary>
        public Action<string>? Error { get; init; }
    }
}
