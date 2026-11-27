using System.Collections.Generic;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core
{
    /// <summary>Summary of the computed stepped insulation layout.</summary>
    public sealed class LayoutResult
    {
        public IReadOnlyList<InsulationCell> Cells { get; set; }

        public int TotalCells { get; set; }
        public int FullCells { get; set; }
        public int TrimmedCells { get; set; }

        /// <summary>Cells skipped because they were below the minimum cut size.</summary>
        public int SkippedCells { get; set; }

        /// <summary>Maximum stepping-layer count across all cells.</summary>
        public int MaxStepLayers { get; set; }

        /// <summary>Panel count per layer: index 0 = base (100 mm), index 1..N = step layers (35 mm each).</summary>
        public IReadOnlyList<int> PanelsPerLayer { get; set; }

        /// <summary>Total number of roof elements to create (base + all step layers).</summary>
        public int TotalPanels { get; set; }

        /// <summary>Total area covered by the base layer (m^2).</summary>
        public double TotalAreaM2 { get; set; }

        /// <summary>Total insulation volume (m^3).</summary>
        public double TotalVolumeM3 { get; set; }

        public IReadOnlyList<string> Errors { get; set; }
        public bool IsValid => Errors == null || Errors.Count == 0;
    }
}
