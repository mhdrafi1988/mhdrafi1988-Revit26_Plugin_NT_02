using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>The only place feet ↔ metres conversion happens (via UnitUtils).</summary>
    public static class UnitConverter
    {
        public static double FeetToMetres(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);

        public static double MetresToFeet(double metres) => UnitUtils.ConvertToInternalUnits(metres, UnitTypeId.Meters);

        public static Vec2 ToPlanMetres(XYZ point) => new Vec2(FeetToMetres(point.X), FeetToMetres(point.Y));

        /// <summary>Plan point (m) → model point (ft) at elevation <paramref name="zFeet"/>.</summary>
        public static XYZ ToModel(Vec2 planMetres, double zFeet) =>
            new XYZ(MetresToFeet(planMetres.X), MetresToFeet(planMetres.Y), zFeet);
    }
}
