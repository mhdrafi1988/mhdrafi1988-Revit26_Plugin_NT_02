// IslandRoofSplitter V002 — Revit-free snapshot of the picked roof + detail lines, built by the Command
// (and again by the handler on Re-pick). The ViewModel and the preview only ever see this.
//
// Update (2026-10-15, per Anas): level and roof type are no longer assumed. The snapshot now says whether
// each was actually detected on the source roof (LevelDetected / RoofTypeDetected), carries every level's
// elevation (LevelOption.Display → "Level 2 · 4000 mm") and a reference Z for the nearest-level fallback
// (RoofTopZFt). Nothing here creates anything — the Command asks, the ViewModel guards Commit.
#nullable enable
using System.Collections.Generic;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Models
{
    public sealed record NamedId(long Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>A level the new roofs can sit on. Elevation is Revit internal units (ft); Display is mm.</summary>
    public sealed record LevelOption(long Id, string Name, double ElevationFt)
    {
        public double ElevationMm => LengthUnits.FtToMm(ElevationFt);

        /// <summary>Bound by the Level combo boxes (DisplayMemberPath="Display").</summary>
        public string Display => $"{Name} · {ElevationMm:0} mm";

        public override string ToString() => Display;
    }

    public sealed class SplitterContext
    {
        public long RoofId { get; init; }
        public string RoofTitle { get; init; } = "";
        public string RoofDetail { get; init; } = "";
        public long RoofTypeId { get; init; }
        public long LevelId { get; init; }
        public double BaseOffsetFt { get; init; }
        public RoofSurfaceMesh Mesh { get; init; } = new();

        /// <summary>False when the source roof has no usable Level (unset, or the Level was deleted).</summary>
        public bool LevelDetected { get; init; }

        /// <summary>False when the source roof's type could not be read as a RoofType.</summary>
        public bool RoofTypeDetected { get; init; }

        /// <summary>Lowest Z of the roof's top faces (ft), used to pre-select the nearest level when
        /// the level was not detected. Null when neither the mesh nor a bounding box gave one.</summary>
        public double? RoofTopZFt { get; init; }

        public List<SourceSegment> Segments { get; init; } = new();
        public string LinesTitle { get; init; } = "";
        public string LinesDetail { get; init; } = "";

        public List<NamedId> RoofTypes { get; init; } = new();
        public List<LevelOption> Levels { get; init; } = new();

        /// <summary>V002: floor types for the fallback / Floor-only output (foundation slabs excluded).</summary>
        public List<NamedId> FloorTypes { get; init; } = new();

        /// <summary>The document's default floor type, when it has one. 0 = none.</summary>
        public long DefaultFloorTypeId { get; init; }

        /// <summary>Nothing can be created without at least one of each — the Command stops with a message.</summary>
        public bool HasLevels => Levels.Count > 0;
        public bool HasRoofTypes => RoofTypes.Count > 0;
        public bool HasFloorTypes => FloorTypes.Count > 0;

        /// <summary>Application.ShortCurveTolerance (ft).</summary>
        public double ShortCurveTolFt { get; init; } = 0.00256;
    }
}
