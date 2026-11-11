using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// Turns structural elements into triangle-mesh snapshots in host millimetres, with outward-facing
    /// triangles as the clash engine requires.
    /// </summary>
    public static class StructuralExtractor
    {
        /// <summary>Reads the elements of the given categories from <paramref name="document"/>.</summary>
        /// <param name="document">The model to read (host or linked document).</param>
        /// <param name="toHost">Transform of that model into the host; identity for the host.</param>
        /// <param name="categories">Categories to read.</param>
        /// <param name="outline">When given (in the model's own coordinates), only elements whose box touches it are read.</param>
        /// <param name="notes">Receives one line per category for elements with no solid geometry.</param>
        public static List<StructuralElementData> Read(Document document, Transform toHost,
            IEnumerable<StructuralCategory> categories, Outline outline, ICollection<string> notes)
        {
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
            var result = new List<StructuralElementData>();

            foreach (StructuralCategory category in categories)
            {
                int skipped = 0;
                FilteredElementCollector collector = new FilteredElementCollector(document)
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(CategoryMap.Of(category))))
                    .WhereElementIsNotElementType();
                if (outline != null)
                    collector = collector.WherePasses(new BoundingBoxIntersectsFilter(outline));

                foreach (Element element in collector)
                {
                    var triangles = new List<Triangle>();
                    GeometryElement geometry = element.get_Geometry(options);
                    if (geometry != null)
                        Collect(geometry, toHost, triangles);

                    if (triangles.Count < 4)
                    {
                        skipped++;
                        continue;
                    }

                    MakeOutward(triangles);
                    var data = new StructuralElementData(element.UniqueId, element.Id.Value, category, new TriangleMesh(triangles));
                    SetOrientation(data, element, toHost);
                    result.Add(data);
                }

                if (skipped > 0)
                    notes?.Add($"{skipped} element(s) in {CategoryCatalog.NameOf(category)} have no solid geometry and were skipped.");
            }
            return result;
        }

        private static void Collect(GeometryElement geometry, Transform toHost, List<Triangle> triangles)
        {
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                    AddSolid(solid, toHost, triangles);
                else if (item is GeometryInstance instance)
                    Collect(instance.GetInstanceGeometry(), toHost, triangles);
            }
        }

        private static void AddSolid(Solid solid, Transform toHost, List<Triangle> triangles)
        {
            foreach (Face face in solid.Faces)
            {
                Mesh mesh = face.Triangulate();
                if (mesh == null)
                    continue;
                XYZ planarNormal = (face as PlanarFace)?.FaceNormal;

                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle triangle = mesh.get_Triangle(i);
                    XYZ a = triangle.get_Vertex(0), b = triangle.get_Vertex(1), c = triangle.get_Vertex(2);
                    XYZ normal = (b - a).CrossProduct(c - a);
                    if (normal.GetLength() < 1e-12)
                        continue;

                    // Wind the triangle so its normal agrees with the face normal.
                    XYZ faceNormal = planarNormal ?? NormalAt(face, (a + b + c) / 3);
                    if (faceNormal != null && normal.DotProduct(faceNormal) < 0)
                    {
                        XYZ swap = b; b = c; c = swap;
                    }
                    triangles.Add(new Triangle(UnitsMm.Point(a, toHost), UnitsMm.Point(b, toHost), UnitsMm.Point(c, toHost)));
                }
            }
        }

        private static XYZ NormalAt(Face face, XYZ point)
        {
            IntersectionResult projection = face.Project(point);
            return projection == null ? null : face.ComputeNormal(projection.UVPoint);
        }

        /// <summary>
        /// Safety net for the winding: a closed, outward-wound mesh has a positive signed volume. A
        /// negative one means every triangle faces inward, so all are flipped.
        /// </summary>
        private static void MakeOutward(List<Triangle> triangles)
        {
            Vec3 reference = triangles[0].A;
            double volume = 0;
            foreach (Triangle t in triangles)
                volume += (t.A - reference).Dot((t.B - reference).Cross(t.C - reference));
            if (volume >= 0)
                return;
            for (int i = 0; i < triangles.Count; i++)
                triangles[i] = new Triangle(triangles[i].A, triangles[i].C, triangles[i].B);
        }

        private static void SetOrientation(StructuralElementData data, Element element, Transform toHost)
        {
            switch (element)
            {
                case Wall wall when (wall.Location as LocationCurve)?.Curve is Line:
                    data.FaceNormal = UnitsMm.Direction(wall.Orientation, toHost);
                    break;
                case Floor _:
                    data.FaceNormal = UnitsMm.Direction(XYZ.BasisZ, toHost);
                    break;
                case FamilyInstance instance when data.Category == StructuralCategory.StructuralFraming
                                                || data.Category == StructuralCategory.StructuralColumn:
                    if ((instance.Location as LocationCurve)?.Curve is Line line)
                        data.MemberAxis = UnitsMm.Direction(line.Direction, toHost);
                    else if (data.Category == StructuralCategory.StructuralColumn)
                        data.MemberAxis = UnitsMm.Direction(XYZ.BasisZ, toHost);
                    break;
            }
        }
    }
}
