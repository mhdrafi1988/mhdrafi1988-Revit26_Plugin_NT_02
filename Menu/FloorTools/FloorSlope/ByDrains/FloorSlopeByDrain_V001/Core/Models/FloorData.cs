// File: FloorData.cs
// Location: Core/Models/
// Ported from AutoSlopeByDrain V004 (roof) — unchanged.

using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models
{
    public class FloorData
    {
        public Floor Floor { get; set; }
        public Face TopFace { get; set; }
        public List<SlabShapeVertex> Vertices { get; set; }
        public List<DrainItem> DetectedDrains { get; set; }

        public FloorData()
        {
            Vertices = new List<SlabShapeVertex>();
            DetectedDrains = new List<DrainItem>();
        }
    }
}
