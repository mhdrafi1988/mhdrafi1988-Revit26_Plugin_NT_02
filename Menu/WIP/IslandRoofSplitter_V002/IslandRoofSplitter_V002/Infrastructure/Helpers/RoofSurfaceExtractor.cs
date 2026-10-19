// IslandRoofSplitter V002 — triangulates the source roof's top faces once, so Core can sample Z without Revit.
#nullable enable
using Autodesk.Revit.DB;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class RoofSurfaceExtractor
    {
        public static RoofSurfaceMesh Extract(RoofBase roof)
        {
            var mesh = new RoofSurfaceMesh();
            int faceIndex = 0;
            foreach (Reference r in HostObjectUtils.GetTopFaces(roof))
            {
                if (roof.GetGeometryObjectFromReference(r) is not Face face) continue;
                Mesh m = face.Triangulate();
                if (m == null) continue;
                for (int i = 0; i < m.NumTriangles; i++)
                {
                    Autodesk.Revit.DB.MeshTriangle t = m.get_Triangle(i);
                    mesh.Triangles.Add(new Core.Models.MeshTriangle(
                        P(t.get_Vertex(0)), P(t.get_Vertex(1)), P(t.get_Vertex(2)), faceIndex));
                }
                faceIndex++;
            }
            return mesh;
        }

        private static Pt3 P(XYZ p) => new(p.X, p.Y, p.Z);
    }
}
