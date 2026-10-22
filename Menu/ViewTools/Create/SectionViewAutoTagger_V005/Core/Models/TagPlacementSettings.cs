namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V005
{
    /// <summary>
    /// Global tag placement settings, persisted to
    /// %AppData%\Revit26_Plugin_Revised\SectionViewAutoTagger\settings.json.
    /// Applies uniformly to every view in the worklist (confirmed: global,
    /// not per-view). Plain POCO for System.Text.Json.
    ///
    /// V003: added LeaderEndCondition (global, confirmed — NOT per-category;
    /// tag type is the only per-category setting). Default is Free.
    /// </summary>
    public class TagPlacementSettings
    {
        public AlignmentSide AlignmentSide { get; set; } = AlignmentSide.Left;

        /// <summary>Offset from the view's crop boundary to the alignment line, in millimeters.</summary>
        public double OffsetMm { get; set; } = 50.0;

        /// <summary>Fixed vertical spacing between stacked tags, in millimeters.</summary>
        public double SpacingMm { get; set; } = 25.0;

        /// <summary>Global leader-end condition applied to every placed tag. Default: Free.</summary>
        public LeaderEndCondition LeaderEndCondition { get; set; } = LeaderEndCondition.Free;
    }
}
