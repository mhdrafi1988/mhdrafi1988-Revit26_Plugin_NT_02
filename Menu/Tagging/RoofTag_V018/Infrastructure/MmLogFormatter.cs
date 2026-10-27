using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.RoofTag.V018
{
    /// <summary>Formats model points for the log, in millimetres.</summary>
    internal static class MmLogFormatter
    {
        /// <summary>"(x, y, z)" with each coordinate converted from feet to mm, one decimal.</summary>
        public static string Point(XYZ p) =>
            $"({UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters):0.0}, " +
            $"{UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters):0.0}, " +
            $"{UnitUtils.ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters):0.0})";

        /// <summary>Converts millimetres to Revit internal units (feet).</summary>
        public static double ToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    }
}
