// IslandRoofSplitter V002 — persisted options (JSON). Roof type / level / floor type / offset / Mark prefix
// are deliberately NOT persisted: they default from the source roof (and the document default) on every launch.
// V002 adds Output (Roof / Roof-then-Floor / Floor). Settings key is unchanged, so V001's saved options carry
// over; unknown members are ignored by SettingsService, and V001 ignores Output.
#nullable enable
namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models
{
    public sealed class IslandSplitterSettings
    {
        public SplitMode Mode { get; set; } = SplitMode.Sloped;

        /// <summary>V002. Default: try the roof, fall back to a floor so an island is never silently lost.</summary>
        public OutputElement Output { get; set; } = OutputElement.RoofThenFloor;

        public NestingMode Nesting { get; set; } = NestingMode.Ring;
        public CrossingMode Crossings { get; set; } = CrossingMode.Split;
        public ArcOutput Arcs { get; set; } = ArcOutput.TrueArcs;
        public InteriorPoints Interior { get; set; } = InteriorPoints.RidgeValleyPoints;

        /// <summary>Chord (sagitta) tolerance for arc tessellation. Per Anas (2026-10-14): default 5 mm, range 1–50.</summary>
        public double ChordToleranceMm { get; set; } = 5.0;

        public const double MinChordToleranceMm = 1.0;
        public const double MaxChordToleranceMm = 50.0;

        /// <summary>Endpoint gap healing. Fixed by spec at 1 mm (not user-editable).</summary>
        public const double HealToleranceMm = 1.0;

        public IslandSplitterSettings Clamp()
        {
            if (double.IsNaN(ChordToleranceMm)) ChordToleranceMm = 5.0;
            if (ChordToleranceMm < MinChordToleranceMm) ChordToleranceMm = MinChordToleranceMm;
            if (ChordToleranceMm > MaxChordToleranceMm) ChordToleranceMm = MaxChordToleranceMm;
            return this;
        }

        public IslandSplitterSettings Clone() => (IslandSplitterSettings)MemberwiseClone();
    }
}
