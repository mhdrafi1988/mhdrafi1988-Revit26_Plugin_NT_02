using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>
    /// The one place millimetres meet Revit's internal feet, so a unit change stays isolated
    /// (CLAUDE.md "Keep Revit-version-specific API calls behind one helper").
    /// </summary>
    public static class UnitsMm
    {
        /// <summary>Internal units (feet) to millimetres.</summary>
        public static double FromFeet(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

        /// <summary>Millimetres to internal units (feet).</summary>
        public static double ToFeet(double millimetres) => UnitUtils.ConvertToInternalUnits(millimetres, UnitTypeId.Millimeters);
    }
}
