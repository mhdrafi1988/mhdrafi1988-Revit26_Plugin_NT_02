using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry;
using Revit26_Plugin_Revised.IsolatedSection.V001.Core.Models;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Infrastructure.Revit
{
    /// <summary>
    /// Reads elements into <see cref="PickedElement"/>s: checks they are model elements, and collects
    /// their geometry points in host project coordinates (applying the link transform for linked elements).
    /// </summary>
    public static class ElementGeometryReader
    {
        // Fine detail, so the footprint matches what the 3D view will show.
        private static readonly Options GeometryOptions = new()
        {
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = false,
        };

        /// <summary>
        /// True when the tool can use <paramref name="e"/>: a placed 3D model element (not a type, view,
        /// annotation, level/grid, room/area/space or link instance). Otherwise <paramref name="reason"/> says why.
        /// </summary>
        public static bool IsUsable(Element e, out string reason)
        {
            reason = null;
            if (e == null) reason = "it no longer exists";
            else if (e is ElementType) reason = "it is a type, not a placed element";
            else if (e is View) reason = "it is a view";
            else if (e is RevitLinkInstance) reason = "it is a whole link — use \"Pick in link\" to pick elements inside it";
            else if (e is DatumPlane) reason = "levels, grids and reference planes have no 3D shape";
            else if (e is SpatialElement) reason = "rooms, areas and spaces have no 3D shape";
            else if (e.ViewSpecific) reason = "it belongs to one view (annotation or detail)";
            else if (e.Category == null || e.Category.CategoryType != CategoryType.Model) reason = "it is not a model element";
            else if (e.get_BoundingBox(null) == null) reason = "it has no 3D extent";
            return reason == null;
        }

        /// <summary>
        /// Reads <paramref name="e"/>; <paramref name="link"/> is the link instance holding it, or null for a
        /// host-model element. Returns null (with <paramref name="reason"/>) when it cannot be used.
        /// </summary>
        public static PickedElement Read(Element e, RevitLinkInstance link, out string reason)
        {
            if (!IsUsable(e, out reason)) return null;

            Transform toHost = link?.GetTotalTransform() ?? Transform.Identity;
            ElementFootprint footprint = ElementFootprint.FromPoints(CollectPoints(e, toHost));
            if (footprint == null)
            {
                reason = "no geometry could be read";
                return null;
            }

            return new PickedElement(
                e.Id.Value,
                link?.Id.Value ?? -1,
                link?.Name,
                e.Category?.Name,
                Describe(e),
                footprint);
        }

        /// <summary>"Family : Type" for family instances, otherwise the element or type name.</summary>
        public static string Describe(Element e)
        {
            if (e is FamilyInstance fi && fi.Symbol != null) return $"{fi.Symbol.FamilyName} : {fi.Symbol.Name}";
            ElementId typeId = e.GetTypeId();
            if (typeId != ElementId.InvalidElementId && e.Document.GetElement(typeId) is ElementType t)
                return string.IsNullOrEmpty(t.FamilyName) ? t.Name : $"{t.FamilyName} : {t.Name}";
            return e.Name;
        }

        /// <summary>
        /// Geometry points of <paramref name="e"/> mapped by <paramref name="toHost"/>: solid edges (or face
        /// meshes for edgeless faces), meshes, curves and points. Falls back to the bounding-box corners.
        /// </summary>
        public static List<Point3> CollectPoints(Element e, Transform toHost)
        {
            var points = new List<Point3>();
            GeometryElement geometry = e.get_Geometry(GeometryOptions);
            if (geometry != null) Collect(geometry, toHost, points);

            if (points.Count == 0 && e.get_BoundingBox(null) is BoundingBoxXYZ bb)
            {
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                    Add(points, toHost.OfPoint(bb.Transform.OfPoint(new XYZ(x, y, z))));
            }
            return points;
        }

        /// <summary>
        /// Outward normal, in host coordinates, of the face of <paramref name="e"/> nearest to
        /// <paramref name="globalPoint"/> (a picked point). Null when no face is found.
        /// </summary>
        public static XYZ FaceNormalAt(Element e, XYZ globalPoint)
        {
            GeometryElement geometry = e.get_Geometry(GeometryOptions);
            if (geometry == null) return null;

            XYZ best = null;
            double bestDistance = double.MaxValue;
            foreach (Face face in Faces(geometry))
            {
                IntersectionResult hit = face.Project(globalPoint);
                if (hit == null || hit.Distance >= bestDistance) continue;
                bestDistance = hit.Distance;
                best = face.ComputeNormal(hit.UVPoint);
            }
            return best;
        }

        private static IEnumerable<Face> Faces(GeometryElement geometry)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face f in solid.Faces) yield return f;
                }
                else if (obj is GeometryInstance instance && instance.GetInstanceGeometry() is GeometryElement nested)
                {
                    foreach (Face f in Faces(nested)) yield return f;
                }
            }
        }

        private static void Collect(GeometryElement geometry, Transform toHost, List<Point3> points)
        {
            foreach (GeometryObject obj in geometry)
            {
                switch (obj)
                {
                    case Solid solid when solid.Faces.Size > 0:
                        foreach (Edge edge in solid.Edges)
                            foreach (XYZ p in edge.Tessellate()) Add(points, toHost.OfPoint(p));
                        foreach (Face face in solid.Faces)
                        {
                            if (face.EdgeLoops.Size > 0) continue; // its edges are already in
                            Mesh faceMesh = face.Triangulate();
                            if (faceMesh == null) continue;
                            foreach (XYZ p in faceMesh.Vertices) Add(points, toHost.OfPoint(p));
                        }
                        break;
                    case Mesh mesh:
                        foreach (XYZ p in mesh.Vertices) Add(points, toHost.OfPoint(p));
                        break;
                    case Curve curve when curve.IsBound:
                        foreach (XYZ p in curve.Tessellate()) Add(points, toHost.OfPoint(p));
                        break;
                    case PolyLine polyLine:
                        foreach (XYZ p in polyLine.GetCoordinates()) Add(points, toHost.OfPoint(p));
                        break;
                    case Point point:
                        Add(points, toHost.OfPoint(point.Coord));
                        break;
                    case GeometryInstance instance:
                        // GetInstanceGeometry is already in the element's model coordinates.
                        GeometryElement nested = instance.GetInstanceGeometry();
                        if (nested != null) Collect(nested, toHost, points);
                        break;
                }
            }
        }

        private static void Add(List<Point3> points, XYZ p) => points.Add(new Point3(p.X, p.Y, p.Z));
    }
}
