// =======================================================
// File: InnerLoopDividerResult.cs
// Location: Core/Models/
// Changes vs V011: exact Apply counts (added / already existing / failed
// / offsets reset), the floor's shape points after Apply, and analysis
// notes. PointsAdded keeps its V011 meaning (-1 on failure).
// =======================================================

using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models
{
    public class InnerLoopDividerResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public InnerLoopDividerOperation Operation { get; set; }

        /// <summary>Analyze: the openings found.</summary>
        public List<FloorLoopModel> Loops { get; set; }

        /// <summary>Analyze: things worth telling the user (unclosed sketch loop, shape editing off …).</summary>
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>Apply: new points added, or -1 on failure.</summary>
        public int PointsAdded { get; set; }

        public int PointsSkippedExisting { get; set; }
        public int PointsFailed { get; set; }
        public int OffsetsReset { get; set; }
        public int LoopsProcessed { get; set; }

        /// <summary>Apply: the floor's shape points afterwards, so the window can refresh its counts without re-analysing.</summary>
        public List<P2> VerticesAfter { get; set; } = new List<P2>();
    }
}
