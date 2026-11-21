namespace Revit26_Plugin_Revised.SheetAutoRearrange.V031.Core.Models
{
    /// <summary>
    /// V031 NEW: how Rearrange treats each view's title (the viewport label). Everything is
    /// off by default, so a settings.json from V030 or older gives the V030 layout.
    /// Only the title's position and line length change. Its text comes from the view's
    /// Title on Sheet and the viewport type, and the tool never edits either.
    /// </summary>
    public class ViewTitleSettings
    {
        /// <summary>Pack each view as its box plus its title, so a title never overlaps the view below it or leaves the usable area.</summary>
        public bool IncludeInFootprint { get; set; }

        /// <summary>Put every title in the same place: left edge on the view's left edge, top <see cref="GapBelowMm"/> below the view.</summary>
        public bool StandardOffset { get; set; }

        /// <summary>Gap between the bottom of the view and the top of its title, in sheet mm (used with <see cref="StandardOffset"/>).</summary>
        public double GapBelowMm { get; set; } = 3.0;

        /// <summary>Make each title's extension line as long as its view is wide.</summary>
        public bool LineMatchesWidth { get; set; }

        /// <summary>
        /// Titles in one row sit on one line: views in a row are bottom-aligned, and every
        /// footprint gets the same title band height. Needs <see cref="StandardOffset"/>.
        /// </summary>
        public bool AlignRowBaselines { get; set; }

        /// <summary>True when at least one option changes the title or the footprint.</summary>
        public bool AnyOn => IncludeInFootprint || StandardOffset || LineMatchesWidth || AlignRowBaselines;

        /// <summary>Baseline alignment only works on top of the standard offset.</summary>
        public bool BaselinesActive => AlignRowBaselines && StandardOffset;

        /// <summary>A copy, so a Run is not affected by later edits in the window.</summary>
        public ViewTitleSettings Clone() => (ViewTitleSettings)MemberwiseClone();
    }
}
