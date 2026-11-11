using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// The one place where Revit internal units (feet) and coordinates meet the tool's millimetres.
    /// </summary>
    public static class UnitsMm
    {
        private const double MmPerFoot = 304.8;

        /// <summary>Feet to millimetres.</summary>
        public static double ToMm(double feet) => feet * MmPerFoot;

        /// <summary>Millimetres to feet.</summary>
        public static double ToFeet(double mm) => mm / MmPerFoot;

        /// <summary>A Revit point (feet) as a millimetre point, after applying <paramref name="transform"/>.</summary>
        public static Vec3 Point(XYZ point, Transform transform)
        {
            XYZ p = transform == null ? point : transform.OfPoint(point);
            return new Vec3(p.X * MmPerFoot, p.Y * MmPerFoot, p.Z * MmPerFoot);
        }

        /// <summary>A Revit direction as a unit vector, after applying <paramref name="transform"/>.</summary>
        public static Vec3 Direction(XYZ vector, Transform transform)
        {
            XYZ v = transform == null ? vector : transform.OfVector(vector);
            return new Vec3(v.X, v.Y, v.Z).Normalized();
        }

        /// <summary>A millimetre point as a Revit point (feet).</summary>
        public static XYZ ToXyz(Vec3 mm) => new XYZ(mm.X / MmPerFoot, mm.Y / MmPerFoot, mm.Z / MmPerFoot);

        /// <summary>A unit vector as a Revit vector.</summary>
        public static XYZ ToXyzDirection(Vec3 direction) => new XYZ(direction.X, direction.Y, direction.Z).Normalize();

        /// <summary>A Revit transform (feet) as a millimetre transform.</summary>
        public static Transform3 ToTransform3(Transform transform)
        {
            if (transform == null) return Transform3.Identity;
            return new Transform3(Point(transform.Origin, null),
                Direction(transform.BasisX, null), Direction(transform.BasisY, null), Direction(transform.BasisZ, null));
        }

        /// <summary>
        /// A millimetre box in host coordinates as a Revit outline in the coordinates of a model whose
        /// transform to the host is <paramref name="toHost"/>.
        /// </summary>
        public static Outline ToOutline(Aabb hostMm, Transform toHost)
        {
            Transform toModel = toHost == null ? Transform.Identity : toHost.Inverse;
            var corners = new List<XYZ>();
            foreach (double x in new[] { hostMm.Min.X, hostMm.Max.X })
                foreach (double y in new[] { hostMm.Min.Y, hostMm.Max.Y })
                    foreach (double z in new[] { hostMm.Min.Z, hostMm.Max.Z })
                        corners.Add(toModel.OfPoint(ToXyz(new Vec3(x, y, z))));

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (XYZ c in corners)
            {
                if (c.X < minX) minX = c.X; if (c.Y < minY) minY = c.Y; if (c.Z < minZ) minZ = c.Z;
                if (c.X > maxX) maxX = c.X; if (c.Y > maxY) maxY = c.Y; if (c.Z > maxZ) maxZ = c.Z;
            }
            return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }
    }
}
