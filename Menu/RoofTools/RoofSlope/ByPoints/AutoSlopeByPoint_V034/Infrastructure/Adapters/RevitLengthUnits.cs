// =======================================================
// File: RevitLengthUnits.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
// New in V034. ILengthUnits through Revit's UnitUtils (via RevitConvert),
// so the runner's numbers match V033 exactly.
// =======================================================

using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
{
    /// <summary>Length conversions done by Revit's UnitUtils.</summary>
    internal sealed class RevitLengthUnits : ILengthUnits
    {
        /// <inheritdoc/>
        public double MToFt(double m) => RevitConvert.MToFt(m);

        /// <inheritdoc/>
        public double MmToFt(double mm) => RevitConvert.MmToFt(mm);

        /// <inheritdoc/>
        public double FtToM(double ft) => RevitConvert.FtToM(ft);
    }
}
