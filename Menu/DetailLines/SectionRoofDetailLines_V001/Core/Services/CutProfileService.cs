using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
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
        public List<Curve> GetCutCurves(IEnumerable<Solid> solids, Plane cutPlane, Action<string> onWarning)
        {
            var curves = new List<Curve>();
            if (solids == null || cutPlane == null) return curves;

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
                            if (curve != null) curves.Add(curve);
                }
            }

            return curves;
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
