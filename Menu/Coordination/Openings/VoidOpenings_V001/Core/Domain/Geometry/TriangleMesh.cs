using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>
    /// Surface of a structural element as triangles. Contract for whoever builds one: the surface is
    /// closed (it bounds one or more solids) and every triangle is wound so its normal points outward.
    /// The clash engine relies on that to tell entering from leaving.
    /// </summary>
    public sealed class TriangleMesh
    {
        /// <summary>The triangles.</summary>
        public IReadOnlyList<Triangle> Triangles { get; }

        /// <summary>Bounding box of the whole mesh.</summary>
        public Aabb Bounds { get; }

        /// <summary>Creates a mesh. Throws when there are no triangles.</summary>
        public TriangleMesh(IEnumerable<Triangle> triangles)
        {
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            Triangles = triangles.ToList();
            if (Triangles.Count == 0) throw new ArgumentException("A mesh needs at least one triangle.", nameof(triangles));
            Bounds = Aabb.FromPoints(Triangles.SelectMany(t => new[] { t.A, t.B, t.C }));
        }
    }
}
