// =======================================================
// File: CreaserAdvResult.cs
// Location: Core/Models/
// Returned by CreaserAdvEngine.Execute — crosses back over the
// ExternalEvent boundary via CreaserAdvPayload.OnCompleted.
// =======================================================

namespace Revit26_Plugin_Revised.FloorCreaser.V001.Core.Models
{
    public class CreaserAdvResult
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }

        public int CreasesFound { get; set; }
        public int BoundaryFound { get; set; }
        public int Created { get; set; }
        public int Failed { get; set; }
        public int RidgePoints { get; set; }
        public int DisconnectedPoints { get; set; }
    }
}
