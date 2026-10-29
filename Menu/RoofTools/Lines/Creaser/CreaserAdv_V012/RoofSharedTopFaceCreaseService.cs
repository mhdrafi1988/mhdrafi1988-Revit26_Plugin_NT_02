// ============================================================
// File: RoofSharedTopFaceCreaseService.cs
// Namespace: Revit26_Plugin_Revised.CreaserAdv.V012.Services
// ============================================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.CreaserAdv.V012.Services
{
    /// <summary>Crease and boundary curves pulled from one roof in a single geometry pass.</summary>
    public sealed class RoofCurveSet
    {
        public IList<Curve> Creases  { get; } = new List<Curve>();
        public IList<Curve> Boundary { get; } = new List<Curve>();
    }

    /// <summary>
    /// Extracts roof crease curves and, optionally, boundary curves
    /// by inspecting solid-topology edges.
    ///
    /// Crease edge  = neither adjacent face is a side face AND
    ///                at least one adjacent face is a top face.
    ///
    /// Boundary edge = exactly one adjacent face is a top face AND
    ///                 the other is a side face.
    ///
    /// Works for planar, cylindrical, conical, and freeform roofs by
    /// sampling face normals over a UV grid.
    ///
    /// V010: one geometry pass classifies each edge once for both sets (V009
    /// walked and re-classified every edge twice when boundary lines were
    /// on); solids nested inside <see cref="GeometryInstance"/>s are now
    /// included; and every stage reports counts to the UI log so an empty
    /// result can be traced to its cause.
    ///
    /// 12.0.3: each face is classified once per solid, sampling its normals
    /// once for both tests. Before, both faces of every edge were sampled
    /// twice (top test, side test) for every edge they touch.
    /// </summary>
    public sealed class RoofSharedTopFaceCreaseService
    {
        private readonly LoggingService _log;

        private const double UpwardMajorityThreshold   = 0.7;
        private const double VerticalMajorityThreshold = 0.7;
        private const double TopFaceMinNormalZ          = 0.3;
        private const double SideFaceMaxNormalZ         = 0.2;
        private const int    SampleCount                = 5;

        public RoofSharedTopFaceCreaseService(LoggingService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // --------------------------------------------------
        // Public API
        // --------------------------------------------------

        /// <summary>
        /// Classifies every solid edge of <paramref name="roof"/> and returns
        /// crease curves (top/top) and, when requested, boundary curves (top/side).
        /// </summary>
        public RoofCurveSet Extract(Element roof, bool includeBoundary)
        {
            var set = new RoofCurveSet();

            var solids = new List<Solid>();
            CollectSolids(roof?.get_Geometry(new Options { ComputeReferences = true, IncludeNonVisibleObjects = false }), solids);

            if (roof == null || solids.Count == 0)
            {
                _log.Warning("Roof has no solid geometry in this view/detail level — nothing to extract. " +
                             "Check that the roof is visible in the current view and is not empty.");
                return set;
            }

            int edgesTotal = 0, degenerate = 0, topTop = 0, topSide = 0, sideSide = 0, underside = 0, unclassified = 0;
            int faceCount = 0;

            foreach (Solid solid in solids)
            {
                faceCount += solid.Faces.Size;
                var faceKinds = new FaceKindCache(solid);

                foreach (Edge edge in solid.Edges)
                {
                    edgesTotal++;

                    Curve curve = edge.AsCurve();
                    if (curve == null || curve.Length < 1e-6) { degenerate++; continue; }

                    FaceKind k0 = faceKinds.Get(edge.GetFace(0));
                    FaceKind k1 = faceKinds.Get(edge.GetFace(1));

                    bool f0Side = k0.IsSide, f1Side = k1.IsSide;
                    bool f0Top  = k0.IsTop,  f1Top  = k1.IsTop;

                    if (f0Side && f1Side)
                    {
                        sideSide++;
                    }
                    else if (f0Side || f1Side)
                    {
                        if (f0Top || f1Top)
                        {
                            topSide++;
                            if (includeBoundary)
                                set.Boundary.Add(NormalizeOrientation(curve));
                        }
                        else
                        {
                            underside++; // side/bottom edge — not part of the top surface
                        }
                    }
                    else if (f0Top || f1Top)
                    {
                        topTop++;
                        set.Creases.Add(NormalizeOrientation(curve));
                    }
                    else
                    {
                        unclassified++; // neither top nor side (e.g. underside/underside)
                    }
                }
            }

            _log.Info($"Roof geometry: {solids.Count} solid(s), {faceCount} face(s), {edgesTotal} edge(s).");
            _log.Debug($"  Edge classification — top/top (creases): {topTop}, top/side (boundary): {topSide}, " +
                       $"side/side: {sideSide}, side/underside: {underside}, underside/other: {unclassified}, degenerate: {degenerate}.");
            _log.Info($"Crease edges extracted: {set.Creases.Count}");

            if (includeBoundary)
                _log.Info($"Boundary edges extracted: {set.Boundary.Count}");

            return set;
        }

        // --------------------------------------------------
        // Geometry traversal
        // --------------------------------------------------

        private static void CollectSolids(GeometryElement geom, List<Solid> solids)
        {
            if (geom == null) return;

            foreach (GeometryObject obj in geom)
            {
                switch (obj)
                {
                    case Solid solid when !solid.Edges.IsEmpty:
                        solids.Add(solid);
                        break;

                    case GeometryInstance instance:
                        // Instance geometry is already transformed into model coordinates.
                        CollectSolids(instance.GetInstanceGeometry(), solids);
                        break;
                }
            }
        }

        // --------------------------------------------------
        // Face classification
        // --------------------------------------------------

        /// <summary>
        /// Top face: at least <see cref="UpwardMajorityThreshold"/> of the sampled normals have
        /// Z &gt; <see cref="TopFaceMinNormalZ"/>. Side face: at least
        /// <see cref="VerticalMajorityThreshold"/> have |Z| &lt; <see cref="SideFaceMaxNormalZ"/>.
        /// A null face is neither.
        /// </summary>
        private readonly struct FaceKind
        {
            public readonly bool IsTop;
            public readonly bool IsSide;

            public FaceKind(bool isTop, bool isSide) { IsTop = isTop; IsSide = isSide; }
        }

        /// <summary>
        /// Classifies every face of one solid once. Edge faces are looked up by their geometry
        /// <see cref="GeometryObject.Id"/>; when the solid's face ids are not unique (or not set),
        /// or an id is not found, the face is classified directly — same result, just slower.
        /// </summary>
        private sealed class FaceKindCache
        {
            private readonly Dictionary<int, FaceKind> _byId;

            public FaceKindCache(Solid solid)
            {
                var byId = new Dictionary<int, FaceKind>();
                foreach (Face face in solid.Faces)
                {
                    int id = face.Id;
                    if (id < 0 || byId.ContainsKey(id)) { byId = null; break; }
                    byId[id] = Classify(face);
                }
                _byId = byId;
            }

            public FaceKind Get(Face face)
            {
                if (face == null) return default;
                if (_byId != null && _byId.TryGetValue(face.Id, out FaceKind kind)) return kind;
                return Classify(face);
            }
        }

        /// <summary>Samples the face's normals on a UV grid once and applies both tests to them.</summary>
        private static FaceKind Classify(Face face)
        {
            BoundingBoxUV bb = face.GetBoundingBox();
            int up = 0, vertical = 0, total = 0;

            for (int i = 0; i < SampleCount; i++)
            {
                for (int j = 0; j < SampleCount; j++)
                {
                    double u = bb.Min.U + (bb.Max.U - bb.Min.U) * i / (SampleCount - 1);
                    double v = bb.Min.V + (bb.Max.V - bb.Min.V) * j / (SampleCount - 1);
                    try
                    {
                        XYZ n = face.ComputeNormal(new UV(u, v));
                        if (n.Z > TopFaceMinNormalZ) up++;
                        if (Math.Abs(n.Z) < SideFaceMaxNormalZ) vertical++;
                        total++;
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RoofSharedTopFaceCreaseService] Classify: ComputeNormal failed at U={u:F3} V={v:F3} — {ex.Message}");
                    }
                }
            }

            if (total == 0) return default;

            return new FaceKind(
                (double)up / total >= UpwardMajorityThreshold,
                (double)vertical / total >= VerticalMajorityThreshold);
        }

        // --------------------------------------------------
        // Orientation normalisation (high-Z start)
        // --------------------------------------------------

        private static Curve NormalizeOrientation(Curve curve)
        {
            XYZ start = curve.GetEndPoint(0);
            XYZ end   = curve.GetEndPoint(1);
            XYZ mid   = curve.Evaluate(0.5, true);

            double avgStartZ = (start.Z + mid.Z) / 2.0;
            double avgEndZ   = (end.Z   + mid.Z) / 2.0;

            return avgStartZ >= avgEndZ ? curve : curve.CreateReversed();
        }
    }
}
