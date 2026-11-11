using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;
using Xunit;

namespace VoidOpenings.Tests
{
    /// <summary>Builders for the small models the tests run on. All lengths are millimetres.</summary>
    internal static class TestData
    {
        public static readonly ModelRef StrRef = new ModelRef("file:str.rvt", "STR");
        public static readonly ModelRef MepRef = new ModelRef("file:mep.rvt", "MEP");

        /// <summary>Closed box with outward-facing triangles.</summary>
        public static List<Triangle> BoxTriangles(Vec3 min, Vec3 max)
        {
            Vec3 P(int x, int y, int z) => new Vec3(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
            var tris = new List<Triangle>();
            void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
            {
                tris.Add(new Triangle(a, b, c));
                tris.Add(new Triangle(a, c, d));
            }
            Quad(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)); // -X
            Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)); // +X
            Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)); // -Y
            Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)); // +Y
            Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)); // -Z
            Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)); // +Z
            return tris;
        }

        public static TriangleMesh Box(Vec3 min, Vec3 max) => new TriangleMesh(BoxTriangles(min, max));

        /// <summary>Wall 200 thick in X, from x = <paramref name="x"/>; 1000 long in Y (0..1000), 3000 high.</summary>
        public static StructuralElementData Wall(string id, double x = 0)
            => new StructuralElementData(id, 100, StructuralCategory.Wall,
                Box(new Vec3(x, 0, 0), new Vec3(x + 200, 1000, 3000))) { FaceNormal = Vec3.UnitX };

        /// <summary>Slab 200 thick, z 0..200.</summary>
        public static StructuralElementData Slab(string id)
            => new StructuralElementData(id, 110, StructuralCategory.Floor,
                Box(new Vec3(-1000, -1000, 0), new Vec3(2000, 1000, 200))) { FaceNormal = Vec3.UnitZ };

        public static MepElementData Round(string id, MepCategory category, double diameter, params Vec3[] path)
        {
            var segments = new List<MepSegment>();
            for (int i = 0; i + 1 < path.Length; i++)
                segments.Add(new MepSegment(new Segment3(path[i], path[i + 1]), MepProfile.Round(diameter)));
            return new MepElementData(id, 200, category, segments);
        }

        public static StructuralModel Structure(params StructuralElementData[] elements) => new StructuralModel(StrRef, elements);

        public static MepModel Mep(params MepElementData[] elements) => new MepModel(MepRef, elements);

        public static void AssertNear(Vec3 expected, Vec3 actual, double tolerance = 0.01)
            => Assert.True((expected - actual).Length <= tolerance, $"expected {expected} but was {actual}");
    }
}
