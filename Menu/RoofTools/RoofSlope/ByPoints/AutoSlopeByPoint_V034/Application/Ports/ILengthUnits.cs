// =======================================================
// File: ILengthUnits.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
// New in V034. Length conversions the runner needs. In Revit they go through
// UnitUtils (Infrastructure/Adapters/RevitLengthUnits), exactly as V033 did,
// so every comparison against these values gives the same answer as before.
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
{
    /// <summary>Converts lengths between metres / millimetres and Revit's internal feet.</summary>
    public interface ILengthUnits
    {
        /// <summary>Metres to feet.</summary>
        double MToFt(double m);

        /// <summary>Millimetres to feet.</summary>
        double MmToFt(double mm);

        /// <summary>Feet to metres.</summary>
        double FtToM(double ft);
    }
}
