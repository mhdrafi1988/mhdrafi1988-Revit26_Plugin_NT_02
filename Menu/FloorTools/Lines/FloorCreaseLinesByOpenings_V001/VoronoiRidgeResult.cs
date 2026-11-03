using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models
{
    /// <summary>
    /// Aggregates all computed outputs from the Voronoi ridge generation pipeline.
    /// Passed from service to service and finally consumed by RidgeCreationService.
    /// </summary>
    public class VoronoiRidgeResult
    {
        // ── Drain grouping ────────────────────────────────────────────────────────
        /// <summary>All drain groups produced by DrainGroupingService.</summary>
        public List<DrainGroup> DrainGroups { get; set; } = new List<DrainGroup>();

        // ── Voronoi raw geometry (2D, pre-clip) ───────────────────────────────────
        /// <summary>
        /// Raw Voronoi edges before clipping.
        /// Each tuple = (Start XY, End XY) — Z is always 0 at this stage.
        /// </summary>
        public List<(XYZ Start, XYZ End)> RawVoronoiEdges { get; set; } = new List<(XYZ, XYZ)>();

        /// <summary>
        /// Raw Voronoi vertices (circumcenters) before boundary filtering.
        /// These are candidate ridge intersection points equidistant from 3+ sites.
        /// </summary>
        public List<XYZ> RawVoronoiVertices { get; set; } = new List<XYZ>();

        // ── Clipped / valid geometry ───────────────────────────────────────────────
        /// <summary>
        /// Voronoi edges clipped to the floor boundary.
        /// Each tuple = (Start XYZ, End XYZ) ready for Detail Line creation.
        /// Z coordinate is 0; elevation will be set by SlabShapeEditor separately.
        /// </summary>
        public List<(XYZ Start, XYZ End)> ClippedEdges { get; set; } = new List<(XYZ, XYZ)>();

        /// <summary>
        /// All shape point locations collected from:
        ///   • Voronoi vertices inside boundary
        ///   • Edge-to-edge intersections
        ///   • Edge-to-boundary clip intersections
        ///   • Arc/curve boundary intersections
        /// De-duplicated within snap tolerance before creation.
        /// </summary>
        public List<XYZ> ShapePoints { get; set; } = new List<XYZ>();

        /// <summary>
        /// Maps each ShapePoint index to the DrainGroup indices that contribute to it.
        /// Used by RidgeValidationService to look up distances.
        /// </summary>
        public Dictionary<int, List<int>> ShapePointGroupMap { get; set; } = new Dictionary<int, List<int>>();

        // ── Validation ─────────────────────────────────────────────────────────────
        /// <summary>One entry per ShapePoint, populated by RidgeValidationService.</summary>
        public List<ValidationEntry> ValidationLog { get; set; } = new List<ValidationEntry>();

        /// <summary>Count of points that passed equidistance validation.</summary>
        public int PassCount { get; set; }

        /// <summary>Count of points that failed equidistance validation.</summary>
        public int FailCount { get; set; }

        // ── Revit element IDs (populated after TX-02 and TX-01) ───────────────────
        /// <summary>ElementIds of all created SlabShapeEditor points (after TX-02).</summary>
        public List<ElementId> CreatedShapePointIds { get; set; } = new List<ElementId>();

        /// <summary>ElementIds of all created Detail Lines (after TX-01).</summary>
        public List<ElementId> CreatedDetailLineIds { get; set; } = new List<ElementId>();

        // ── Floor geometry ──────────────────────────────────────────────────────────
        /// <summary>
        /// Centroid of the floor boundary polygon (XY only; Z=0).
        /// Used by RidgeCreationService as a nudge target when AddPoint rejects
        /// a shape point that lands just outside the slab face triangulation.
        /// </summary>
        public XYZ FloorCentroid { get; set; } = XYZ.Zero;

        /// <summary>
        /// Full floor boundary polygon (XY vertices, Z=0).
        /// Used by RidgeCreationService for the nearest-edge inset fallback
        /// when centroid nudging fails (e.g. concave / amoeba-shaped floors).
        /// </summary>
        public List<XYZ> BoundaryPolygon { get; set; } = new List<XYZ>();

        /// <summary>
        /// All interior void/opening loops extracted from the floor sketch or geometry.
        /// Each inner list is one closed polygon (Z=0, tessellated).
        /// Used by RidgeCreationService to place AddPoint calls where ridge lines
        /// intersect inner loop boundaries.
        /// </summary>
        public List<List<XYZ>> InnerLoops { get; set; } = new List<List<XYZ>>();

        /// <summary>
        /// Midpoints of every clipped Voronoi ridge edge (Z=0).
        /// Each is placed as an AddPoint shape-editing point at slab base Z.
        /// </summary>
        public List<XYZ> RidgeEdgeMidPoints { get; set; } = new List<XYZ>();
    }
}