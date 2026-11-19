using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// The one place where Revit's internal feet become the tool's millimetres and back. Snapshots
    /// are always in the active model's coordinates, so a point of a link goes through the link's
    /// transform first.
    /// </summary>
    public static class UnitsMm
    {
        /// <summary>Millimetres per Revit internal unit (one foot).</summary>
        public const double PerFoot = 304.8;

        /// <summary>A length in feet as millimetres.</summary>
        public static double Length(double feet) => feet * PerFoot;

        /// <summary>Millimetres for a length in feet (the same conversion as <see cref="Length"/>).</summary>
        public static double ToMm(double feet) => feet * PerFoot;

        /// <summary>Feet for a length in millimetres.</summary>
        public static double ToFeet(double mm) => mm / PerFoot;

        /// <summary>A point of a model as host millimetres.</summary>
        public static Vec3 Point(XYZ point, Transform toHost)
        {
            XYZ p = toHost.OfPoint(point);
            return new Vec3(p.X * PerFoot, p.Y * PerFoot, p.Z * PerFoot);
        }

        /// <summary>A direction of a model as a unit vector in host coordinates.</summary>
        public static Vec3 Direction(XYZ vector, Transform toHost)
        {
            XYZ v = toHost.OfVector(vector);
            return new Vec3(v.X, v.Y, v.Z).Normalized();
        }

        /// <summary>A point in host millimetres as a Revit point of the host.</summary>
        public static XYZ ToXyz(Vec3 pointMm) => new XYZ(pointMm.X / PerFoot, pointMm.Y / PerFoot, pointMm.Z / PerFoot);
    }
}
