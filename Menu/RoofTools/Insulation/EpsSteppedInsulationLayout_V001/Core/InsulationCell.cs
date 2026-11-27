using System.Collections.Generic;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core
{
    /// <summary>One grid cell's placement data.</summary>
    public sealed class InsulationCell
    {
        /// <summary>Column index (1-based).</summary>
        public int Column { get; set; }

        /// <summary>Row index (1-based).</summary>
        public int Row { get; set; }

        /// <summary>Cell centre in plan (metres).</summary>
        public Vec2 Centre { get; set; }

        /// <summary>Cell boundary corners in plan (metres), world coordinates. 4 for full, 3+ for trimmed.</summary>
        public IReadOnlyList<Vec2> Boundary { get; set; }

        /// <summary>Distance from cell centre to nearest drain (metres).</summary>
        public double DrainDistanceM { get; set; }

        /// <summary>Number of 35 mm stepping layers on top of the 100 mm base.</summary>
        public int StepLayerCount { get; set; }

        /// <summary>Total insulation height: base + steps (mm).</summary>
        public double TotalHeightMm { get; set; }

        /// <summary>True if the cell is trimmed by the roof boundary (not a full rectangle).</summary>
        public bool IsTrimmed { get; set; }

        /// <summary>Serial number assigned during placement.</summary>
        public string Serial { get; set; } = string.Empty;
    }
}
