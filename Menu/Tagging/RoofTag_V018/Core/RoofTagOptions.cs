using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Immutable snapshot of the window settings, so the rules and placement run without WPF.</summary>
    internal sealed class RoofTagOptions
    {
        public bool UseManualMode { get; init; }
        public bool UseLeader { get; init; }
        public bool BendInward { get; init; }
        public double BendOffsetMm { get; init; }
        public double EndOffsetMm { get; init; }
        public ElementId SpotTagTypeId { get; init; }

        public bool InteriorLoopEnabled { get; init; }
        public double InteriorLoopSpacingMm { get; init; }
        public double InteriorLoopToleranceMm { get; init; }

        public bool ExteriorLoopEnabled { get; init; }
        public double ExteriorLoopSpacingMm { get; init; }
        public double ExteriorLoopToleranceMm { get; init; }

        public bool ClusterEnabled { get; init; }
        public double ClusterRadiusMm { get; init; }

        /// <summary>Reads the current window settings.</summary>
        public static RoofTagOptions From(RoofTagViewModel vm) => new()
        {
            UseManualMode = vm.UseManualMode,
            UseLeader = vm.UseLeader,
            BendInward = vm.BendInward,
            BendOffsetMm = vm.BendOffset,
            EndOffsetMm = vm.EndOffset,
            SpotTagTypeId = vm.SelectedSpotTagType?.TagType?.Id,
            InteriorLoopEnabled = vm.InteriorLoopReductionEnabled,
            InteriorLoopSpacingMm = vm.InteriorLoopSpacing,
            InteriorLoopToleranceMm = vm.InteriorLoopBoundaryTolerance,
            ExteriorLoopEnabled = vm.ExteriorLoopReductionEnabled,
            ExteriorLoopSpacingMm = vm.ExteriorLoopSpacing,
            ExteriorLoopToleranceMm = vm.ExteriorLoopBoundaryTolerance,
            ClusterEnabled = vm.ClusterFilterEnabled,
            ClusterRadiusMm = vm.ClusterRadius
        };
    }
}
