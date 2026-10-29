// =======================================================
// File: RevitConvert.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// New in V032: the one place Revit types meet Core types, plus unit
// helpers so call sites read "MmToFt(500)" instead of UnitUtils chains.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    internal static class RevitConvert
    {
        public static Point3 ToPoint3(this XYZ p) => new Point3(p.X, p.Y, p.Z);
        public static XYZ ToXyz(this Point3 p) => new XYZ(p.X, p.Y, p.Z);

        public static double MmToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        public static double MToFt(double m) => UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters);
        public static double FtToM(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters);

        public static Color ToRevitColor(string colorName)
        {
            var (r, g, b) = NamedColors.ToRgb(colorName);
            return new Color(r, g, b);
        }
    }

    /// <summary>A run cannot continue. The message is shown to the user as-is,
    /// and the handler rolls the whole TransactionGroup back.</summary>
    public sealed class AutoSlopeAbortException : System.Exception
    {
        public AutoSlopeAbortException(string message) : base(message) { }
    }
}
