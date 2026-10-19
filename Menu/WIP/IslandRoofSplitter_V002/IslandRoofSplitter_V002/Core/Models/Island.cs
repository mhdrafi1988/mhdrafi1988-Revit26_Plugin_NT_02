// IslandRoofSplitter V002 — one detected closed region (or one unclosed chain reported as Skipped).
#nullable enable
using System.Collections.Generic;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Models
{
    /// <summary>A boundary piece handed to Revit: a line, or a three-point arc (Flat + true arcs only).</summary>
    public readonly record struct LoopEdge(Pt2 Start, Pt2 End, Pt2? Mid)
    {
        public bool IsArc => Mid.HasValue;
    }

    /// <summary>A vertex of a traced loop. ArcIndex / SourceId describe the edge leaving this vertex.</summary>
    public struct LoopVertex
    {
        public Pt2 P;
        public int ArcIndex;   // -1 = straight
        public long SourceId;

        public LoopVertex(Pt2 p, int arcIndex, long sourceId) { P = p; ArcIndex = arcIndex; SourceId = sourceId; }
    }

    public sealed class IslandLoop
    {
        public List<LoopVertex> Vertices { get; set; } = new();
        /// <summary>Edges as they will be sketched (after arc reassembly / crease insertion).</summary>
        public List<LoopEdge> Edges { get; set; } = new();
        /// <summary>Sampled roof Z per sketch vertex (aligned with Edges[i].Start). Null = ray-cast miss.</summary>
        public List<double?> Z { get; set; } = new();
    }

    public sealed class Island
    {
        /// <summary>Island_01, Island_02 … in detection (plan reading) order.</summary>
        public string Name { get; set; } = "";
        public int Number { get; set; }

        public IslandStatus Status { get; set; } = IslandStatus.Ready;
        /// <summary>Why it was skipped / failed, or a short note when Ready/Created.</summary>
        public string Reason { get; set; } = "";

        public IslandLoop Outer { get; set; } = new();
        public List<IslandLoop> Holes { get; } = new();

        /// <summary>Sloped + ridge/valley points: extra shape points strictly inside the island.</summary>
        public List<Pt3> InteriorPoints { get; } = new();

        public HashSet<long> SourceIds { get; } = new();

        public double? ZMin { get; set; }
        public double? ZMax { get; set; }
        public int VertexCount { get; set; }
        public double AreaSqFt { get; set; }

        /// <summary>Plan bounding box (ft) — used for ordering and for zooming in Revit.</summary>
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        /// <summary>Set after Commit.</summary>
        public long? CreatedElementId { get; set; }
    }
}
