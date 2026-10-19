// IslandRoofSplitter V002 — triangulated top faces of the source roof, captured once by the Command.
// Lets Core sample Z with a point-in-triangle test instead of a Revit ray-cast (keeps preview Revit-free).
#nullable enable
using System.Collections.Generic;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Models
{
    public readonly record struct MeshTriangle(Pt3 A, Pt3 B, Pt3 C, int FaceIndex);

    public sealed class RoofSurfaceMesh
    {
        public List<MeshTriangle> Triangles { get; } = new();
    }
}
