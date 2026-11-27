namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core
{
    /// <summary>User-configurable settings for the stepped EPS layout.</summary>
    public sealed class InsulationSettings
    {
        /// <summary>Board size in metres (default 1.2 m = 1200 mm).</summary>
        public double BoardSizeM { get; set; } = 1.2;

        /// <summary>Base layer thickness in millimetres (100 mm).</summary>
        public double BaseThicknessMm { get; set; } = 100.0;

        /// <summary>Stepping layer thickness in millimetres (35 mm).</summary>
        public double StepThicknessMm { get; set; } = 35.0;

        /// <summary>Slope as a percentage (e.g. 1.667 for 1:60). Converted from ratio if needed.</summary>
        public double SlopePercent { get; set; } = 1.667;

        /// <summary>Index of the outline edge the grid U-axis aligns to.</summary>
        public int AlignEdgeIndex { get; set; }

        /// <summary>Grid origin offset along the alignment edge (metres). Shifts the entire grid.</summary>
        public double GridOffsetM { get; set; }

        /// <summary>Minimum trimmed cell dimension in mm. Cells smaller than this are skipped.</summary>
        public double MinCutSizeMm { get; set; } = 100.0;
    }
}
