// IslandRoofSplitter V002 — output of the pure-Core preview run, plus commit request/result POCOs.
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Models
{
    public readonly record struct PreviewMessage(MessageSeverity Severity, string Text);

    public sealed class PreviewResult
    {
        public List<Island> Islands { get; } = new();
        public List<PreviewMessage> Messages { get; } = new();

        public int HealedEndpoints { get; set; }
        public int IgnoredConnectorLines { get; set; }

        public int IslandsDetected => Islands.Count;
        public int VerticesTotal => Islands.Where(i => i.Status != IslandStatus.Skipped).Sum(i => i.VertexCount);
        public int SkippedCount => Islands.Count(i => i.Status == IslandStatus.Skipped);
        public int ReadyCount => Islands.Count(i => i.Status == IslandStatus.Ready);
    }

    /// <summary>Everything the ExternalEvent handler needs to create the elements. Ids are ElementId.Value.</summary>
    public sealed class CommitRequest
    {
        public long SourceRoofId { get; init; }
        public long RoofTypeId { get; init; }
        public long LevelId { get; init; }
        public double BaseOffsetFt { get; init; }
        public string MarkPrefix { get; init; } = "";
        public SplitMode Mode { get; init; }

        /// <summary>V002: roof, roof-with-floor-fallback, or floor.</summary>
        public OutputElement Output { get; init; } = OutputElement.RoofThenFloor;

        /// <summary>V002: type for a floor attempt. 0 when the output is Roof only.</summary>
        public long FloorTypeId { get; init; }

        public List<Island> Islands { get; init; } = new();
    }

    public sealed class IslandCommitResult
    {
        public string Name { get; init; } = "";
        public IslandStatus Status { get; init; }
        public string Reason { get; init; } = "";
        public long? ElementId { get; init; }
        public string Mark { get; init; } = "";

        /// <summary>V002: what was created — Roof, Floor, or None when every attempt failed.</summary>
        public ElementKind Kind { get; init; } = ElementKind.None;

        /// <summary>V002: one line per attempt (what was tried and why it failed), for the log.</summary>
        public List<string> Attempts { get; init; } = new();
    }
}
