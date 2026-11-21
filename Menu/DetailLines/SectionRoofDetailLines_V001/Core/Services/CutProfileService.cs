using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>One edge of a cut profile, and which side of the element it faces.</summary>
    public sealed class CutEdge
    {
        /// <summary>Creates the edge.</summary>
        public CutEdge(Curve curve, ProfileSide side)
        {
            Curve = curve;
            Side = side;
        }

        /// <summary>The edge, in model coordinates, lying in the view's plane.</summary>
        public Curve Curve { get; }

        /// <summary>Which side of the element this edge faces.</summary>
        public ProfileSide Side { get; }
    }

    /// <summary>
    /// Reads an element's solid geometry and works out the profile the section cuts through it.
    /// Read-only — it builds curves in memory and creates nothing in the model.
    /// </summary>
    public sealed class CutProfileService
    {
        /// <summary>How far off the cut plane a face may sit and still count as the cut face,
        /// in feet — about 0.03 mm, tight enough that a parallel face elsewhere on the solid is
        /// never mistaken for the cut.</summary>
        private const double OnPlaneToleranceFeet = 1e-7;

        /// <summary>How parallel a face's normal must be to the cut plane's to count, as the
        /// absolute dot product of the two unit vectors.</summary>
        private const double ParallelTolerance = 1e-6;

        /// <summary>How far off an edge the material probe steps, in feet — about 0.3 mm, well
        /// inside the thinnest real build-up layer and well clear of rounding.</summary>
        private const double ProbeFeet = 0.001;

        /// <summary>cos 45°: at exactly this, an edge is as much a top as a side. Above it the edge
        /// is read as top or bottom, below it as left or right.</summary>
        private const double DiagonalDot = 0.70710678;

        /// <summary>
        /// The solids of <paramref name="element"/>, brought into model coordinates by
        /// <paramref name="toModel"/> (pass null for a host element, which is already there).
        /// Returns an empty list when the element has no solid geometry to read.
        /// </summary>
        public List<Solid> GetSolids(Element element, Transform toModel, Action<string> onWarning)
        {
            var solids = new List<Solid>();
            if (element == null) return solids;

            GeometryElement geometry;
            try
            {
                geometry = element.get_Geometry(new Options
                {
                    DetailLevel = ViewDetailLevel.Fine,
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false
                });
            }
            catch (Exception ex)
            {
                onWarning?.Invoke($"Geometry of element {element.Id.Value} could not be read: {ex.Message}");
                return solids;
            }

            if (geometry == null) return solids;

            CollectSolids(geometry, toModel, solids, 0);
            return solids;
        }

        // Family instances nest their geometry inside GeometryInstances, each with its own
        // transform; the transforms multiply on the way down. The depth guard is a safety net
        // against a pathological nesting depth, not an expected case.
        private void CollectSolids(GeometryElement geometry, Transform toModel, List<Solid> solids, int depth)
        {
            if (depth > 8) return;

            foreach (GeometryObject item in geometry)
            {
                switch (item)
                {
                    case Solid solid when solid.Volume > 1e-9 && solid.Faces.Size > 0:
                        solids.Add(toModel == null ? solid : SolidUtils.CreateTransformed(solid, toModel));
                        break;

                    case GeometryInstance instance:
                    {
                        Transform combined = toModel == null
                            ? instance.Transform
                            : toModel.Multiply(instance.Transform);
                        GeometryElement nested = instance.GetInstanceGeometry();
                        if (nested != null) CollectSolids(nested, combined, solids, depth + 1);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The curves where <paramref name="cutPlane"/> cuts <paramref name="solids"/> — the
        /// element's cut profile, already lying in the plane, so it can go straight into a detail
        /// curve with no projection.
        /// <para>
        /// The solid is cut with the half space on the far side of the plane, which leaves a new
        /// face coplanar with it: that face's edge loops are the profile, outer loop and any
        /// openings alike. A solid the plane misses produces no such face and yields nothing,
        /// which is how an element the section does not actually cut drops out.
        /// </para>
        /// </summary>
        public List<CutEdge> GetCutEdges(
            IEnumerable<Solid> solids, Plane cutPlane, SectionFrame frame, Action<string> onWarning)
        {
            var edges = new List<CutEdge>();
            if (solids == null || cutPlane == null || frame == null) return edges;

            XYZ normal = cutPlane.Normal.Normalize();

            foreach (Solid solid in solids)
            {
                Solid cut;
                try
                {
                    cut = BooleanOperationsUtils.CutWithHalfSpace(solid, cutPlane);
                }
                catch (Exception ex)
                {
                    // Revit refuses the cut for a solid it cannot handle (self-intersecting or
                    // degenerate geometry); that element is reported and left alone.
                    onWarning?.Invoke($"A solid could not be cut on the section plane: {ex.Message}");
                    continue;
                }

                if (cut == null || cut.Faces.Size == 0) continue;

                foreach (Face face in cut.Faces)
                {
                    if (face is not PlanarFace planar) continue;
                    if (!IsOnPlane(planar, cutPlane.Origin, normal)) continue;

                    IList<CurveLoop> loops;
                    try
                    {
                        loops = planar.GetEdgesAsCurveLoops();
                    }
                    catch (Exception ex)
                    {
                        onWarning?.Invoke($"A cut face's edges could not be read: {ex.Message}");
                        continue;
                    }

                    foreach (CurveLoop loop in loops)
                        foreach (Curve curve in loop)
                            if (curve != null)
                                edges.Add(new CutEdge(curve, Classify(planar, curve, frame, normal)));
                }
            }

            return edges;
        }

        /// <summary>
        /// Which side of the element an edge of its cut profile faces.
        /// <para>
        /// The side is read from where the material is, not from where the edge sits in the
        /// profile: a short step probes just off the edge on both sides, and whichever probe lands
        /// on the cut face is the solid side, so the other way is out. That direction, resolved
        /// against the view's up and right, gives the side. It holds for a sloped or stepped roof,
        /// where "the lowest edge" would not, and it reads the head of an opening the section
        /// passes through as a bottom edge, which is what you see standing under it.
        /// </para>
        /// <para>
        /// Where both probes agree — at a corner, or on an edge too short for the step — the
        /// fallback compares the edge with the middle of the cut face, which is right for the
        /// simple profiles that is the only place it happens.
        /// </para>
        /// </summary>
        private static ProfileSide Classify(PlanarFace face, Curve curve, SectionFrame frame, XYZ planeNormal)
        {
            XYZ outward = OutwardDirection(face, curve, planeNormal);
            if (outward == null) return ClassifyByPosition(face, curve, frame);

            double up = outward.DotProduct(frame.Up);
            if (up >= DiagonalDot) return ProfileSide.Top;
            if (up <= -DiagonalDot) return ProfileSide.Bottom;
            return outward.DotProduct(frame.Right) >= 0 ? ProfileSide.Right : ProfileSide.Left;
        }

        private static XYZ OutwardDirection(PlanarFace face, Curve curve, XYZ planeNormal)
        {
            XYZ mid, tangent;
            try
            {
                mid = curve.Evaluate(0.5, true);
                tangent = curve.ComputeDerivatives(0.5, true)?.BasisX;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }

            if (mid == null || tangent == null || tangent.IsZeroLength()) return null;

            XYZ sideways = planeNormal.CrossProduct(tangent.Normalize());
            if (sideways.IsZeroLength()) return null;
            sideways = sideways.Normalize();

            bool plusIsSolid = IsOnFace(face, mid + sideways * ProbeFeet);
            bool minusIsSolid = IsOnFace(face, mid - sideways * ProbeFeet);

            if (plusIsSolid == minusIsSolid) return null; // corner, or an edge shorter than the probe
            return plusIsSolid ? -sideways : sideways;
        }

        private static bool IsOnFace(PlanarFace face, XYZ point)
        {
            try
            {
                IntersectionResult hit = face.Project(point);
                return hit != null && face.IsInside(hit.UVPoint);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
        }

        private static ProfileSide ClassifyByPosition(PlanarFace face, Curve curve, SectionFrame frame)
        {
            XYZ mid;
            try { mid = curve.Evaluate(0.5, true); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return ProfileSide.Top; }

            XYZ centre;
            try
            {
                BoundingBoxUV uv = face.GetBoundingBox();
                centre = face.Evaluate(new UV((uv.Min.U + uv.Max.U) / 2, (uv.Min.V + uv.Max.V) / 2));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return ProfileSide.Top;
            }

            XYZ midBox = frame.ToBox(mid);
            XYZ centreBox = frame.ToBox(centre);
            double dUp = midBox.Y - centreBox.Y;
            double dAcross = midBox.X - centreBox.X;

            if (Math.Abs(dUp) >= Math.Abs(dAcross))
                return dUp >= 0 ? ProfileSide.Top : ProfileSide.Bottom;
            return dAcross >= 0 ? ProfileSide.Right : ProfileSide.Left;
        }

        private static bool IsOnPlane(PlanarFace face, XYZ planeOrigin, XYZ planeNormal)
        {
            XYZ faceNormal = face.FaceNormal;
            if (faceNormal == null || faceNormal.IsZeroLength()) return false;
            if (Math.Abs(Math.Abs(faceNormal.Normalize().DotProduct(planeNormal)) - 1.0) > ParallelTolerance)
                return false;
            return Math.Abs((face.Origin - planeOrigin).DotProduct(planeNormal)) <= OnPlaneToleranceFeet;
        }
    }
}
