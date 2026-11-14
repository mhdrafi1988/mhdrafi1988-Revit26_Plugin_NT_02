// File: IParameterWriter.cs
// Location: Core/Parameters/
// Ported unchanged from AutoSlopeByDrain V004 (roof).

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Parameters
{
    public interface IParameterWriter
    {
        ParameterWriteResult WriteAll(
            Document doc,
            Floor floor,
            DrainExportMetrics metrics,
            double slopePercent,
            double thresholdMeters,
            System.Action<string> logAction = null);
    }

    public struct ParameterWriteResult
    {
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public bool HasFailures => FailCount > 0;
    }
}
