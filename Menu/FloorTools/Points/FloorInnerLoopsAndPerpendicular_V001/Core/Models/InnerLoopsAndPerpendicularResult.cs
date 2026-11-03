// =======================================================
// File: InnerLoopsAndPerpendicularResult.cs
// Location: Core/Models/
// New in V005. Plain result object returned by
// InnerLoopsAndPerpendicularEngine. No UI, no WPF, no ViewModel references.
// =======================================================

using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models
{
    public class InnerLoopsAndPerpendicularResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public InnerLoopsAndPerpendicularOperation Operation { get; set; }

        /// <summary>Populated for Operation.Analyze — inner loops only.</summary>
        public List<FloorLoopModel> Loops { get; set; }

        /// <summary>Populated for Operation.Analyze.</summary>
        public FloorLoopModel OuterLoop { get; set; }

        /// <summary>Populated for Operation.Generate.</summary>
        public List<PerpendicularPointModel> PerpendicularPoints { get; set; }

        /// <summary>Populated for Operation.Apply — number of points added, or -1 on transaction failure.</summary>
        public int PointsAdded { get; set; }
    }
}
