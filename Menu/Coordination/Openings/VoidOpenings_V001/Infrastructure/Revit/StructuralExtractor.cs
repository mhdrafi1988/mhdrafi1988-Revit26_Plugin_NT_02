using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Turns a structural element into a snapshot: its solids as one triangle mesh in host
    /// millimetres, every triangle wound so its normal points out of the solid, plus the face normal
    /// of a wall or floor and the axis of a beam or column.
    /// </summary>
    public static class StructuralExtractor
    {
        private const double MinVolumeCubicFeet = 1e-6;

        /// <summary>Reads one element; returns null and a <paramref name="reason"/> when it has no solid geometry.</summary>
        public static StructuralElementData TryRead(Element element, StructuralCategory category, Transform toHost, out string reason)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (toHost == null) throw new ArgumentNullException(nameof(toHost));

            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
            GeometryElement geometry = element.get_Geometry(options);
            if (geometry == null)
            {
                reason = "It has no geometry.";
                return null;
            }

            var collector = new Collector(toHost);
            collector.Add(geometry);
            if (collector.Triangles.Count == 0)
            {
                reason = "It has no solid geometry.";
                return null;
            }

            reason = null;
            var data = new StructuralElementData(element.UniqueId, element.Id.Value, category, new TriangleMesh(collector.Triangles));

            // Walls and floors: an opening is square to the main face. A curved wall has no single
            // normal; the calculator then uses the face normal where the MEP element enters.
            if ((category == StructuralCategory.Wall || category == StructuralCategory.Floor) && collector.LargestPlanarNormal != null)
                data.FaceNormal = UnitsMm.Direction(collector.LargestPlanarNormal, toHost);

            if (category == StructuralCategory.StructuralFraming || category == StructuralCategory.StructuralColumn)
                data.MemberAxis = MemberAxisOf(element, category, toHost);

            return data;
        }

        private static Vec3? MemberAxisOf(Element element, StructuralCategory category, Transform toHost)
        {
            if ((element.Location as LocationCurve)?.Curve is Line line)
                return UnitsMm.Direction(line.Direction, toHost);
            if (category == StructuralCategory.StructuralColumn && element.Location is LocationPoint)
                return UnitsMm.Direction(XYZ.BasisZ, toHost); // a vertical column
            return null;
        }

        private sealed class Collector
        {
            private readonly Transform _toHost;
            private double _largestArea;
            private bool _largestIsPlanar;

            public Collector(Transform toHost) => _toHost = toHost;

            public List<Triangle> Triangles { get; } = new List<Triangle>();

            /// <summary>Normal of the largest face when that face is planar; null otherwise.</summary>
            public XYZ LargestPlanarNormal { get; private set; }

            public void Add(GeometryElement geometry)
            {
                foreach (GeometryObject item in geometry)
                {
                    if (item is Solid solid)
                    {
                        if (solid.Faces.Size > 0 && solid.Volume > MinVolumeCubicFeet)
                            Add(solid);
                    }
                    else if (item is GeometryInstance instance)
                    {
                        GeometryElement inner = instance.GetInstanceGeometry();
                        if (inner != null)
                            Add(inner);
                    }
                }
            }

            private void Add(Solid solid)
            {
                foreach (Face face in solid.Faces)
                {
                    XYZ planarNormal = (face as PlanarFace)?.FaceNormal;
                    double area = face.Area;
                    if (area > _largestArea)
                    {
                        _largestArea = area;
                        _largestIsPlanar = planarNormal != null;
                        LargestPlanarNormal = _largestIsPlanar ? planarNormal : null;
                    }

                    Mesh mesh = face.Triangulate();
                    if (mesh == null)
                        continue;

                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle triangle = mesh.get_Triangle(i);
                        XYZ a = triangle.get_Vertex(0);
                        XYZ b = triangle.get_Vertex(1);
                        XYZ c = triangle.get_Vertex(2);

                        XYZ winding = (b - a).CrossProduct(c - a);
                        if (winding.GetLength() < 1e-12)
                            continue; // a sliver with no area

                        // The clash engine tells entering from leaving by the triangle's normal, so
                        // every triangle is turned to agree with the face's outward normal.
                        XYZ outward = planarNormal ?? NormalAt(face, (a + b + c) / 3);
                        if (outward != null && winding.DotProduct(outward) < 0)
                        {
                            XYZ swap = b;
                            b = c;
                            c = swap;
                        }

                        Triangles.Add(new Triangle(UnitsMm.Point(a, _toHost), UnitsMm.Point(b, _toHost), UnitsMm.Point(c, _toHost)));
                    }
                }
            }

            private static XYZ NormalAt(Face face, XYZ point)
            {
                try
                {
                    IntersectionResult projection = face.Project(point);
                    return projection?.UVPoint == null ? null : face.ComputeNormal(projection.UVPoint);
                }
                catch (Exception)
                {
                    return null; // keep the winding Revit gave
                }
            }
        }
    }
}
