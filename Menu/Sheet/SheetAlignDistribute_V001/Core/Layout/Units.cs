using System;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout
{
    /// <summary>
    /// Sheet lengths move between two units: Revit's internal feet, which every geometry call uses,
    /// and the millimetres the window shows. Kept in one place so the conversion is never written
    /// out by hand, and Revit-free so the layout tests can use it.
    /// </summary>
    public static class Units
    {
        /// <summary>Millimetres in one foot.</summary>
        public const double MmPerFoot = 304.8;

        /// <summary>Millimetres as Revit feet.</summary>
        public static double MmToFeet(double mm) => mm / MmPerFoot;

        /// <summary>Revit feet as millimetres.</summary>
        public static double FeetToMm(double feet) => feet * MmPerFoot;

        /// <summary>
        /// Rounds <paramref name="mm"/> to the nearest multiple of <paramref name="stepMm"/>, never
        /// below zero. A step of zero or less leaves the value alone (bar the clamp).
        /// </summary>
        public static double RoundToStep(double mm, double stepMm)
        {
            if (stepMm <= 0) return Math.Max(0, mm);
            return Math.Max(0, Math.Round(mm / stepMm, MidpointRounding.AwayFromZero) * stepMm);
        }

        /// <summary>Millimetres for display: one decimal, so a 5.7 mm move reads as 5.7 and not 5.6999.</summary>
        public static double ForDisplay(double mm) => Math.Round(mm, 1, MidpointRounding.AwayFromZero);
    }
}
