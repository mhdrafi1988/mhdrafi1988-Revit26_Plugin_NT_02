using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V006
{
    /// <summary>
    /// Persisted user settings for RoofEdgeSections V005.
    /// Serialized to %AppData%\Revit26_Plugin_Revised\RoofEdgeSections\settings.json via System.Text.Json.
    /// Loaded on window open, saved on window close and after Run.
    ///
    /// V003 → V004 schema change: the dynamic wall-search crop model (OffsetMm,
    /// SearchDistanceMm, MarginOffsetMm, EdgeDepthMm, CropHeightMm) is replaced by
    /// explicit, fixed distances on all three axes relative to the roof edge itself —
    /// no NearbyWallFinder auto-detection. Old settings.json files deserialize cleanly;
    /// removed fields are dropped and new fields take their defaults below.
    /// </summary>
    public class RoofEdgeSectionsSettings
    {
        /// <summary>
        /// How far below the roof edge's own elevation the crop reaches, in millimeters.
        /// (V004 originally split this into separate Offset/Height fields, but since a
        /// single rectangular Revit crop box can't hide a gap between them, they only ever
        /// added together — collapsed into one field.)
        /// </summary>
        public double BelowRoofMm { get; set; } = 600;

        /// <summary>How far above the roof edge's own elevation the crop reaches, in millimeters.</summary>
        public double AboveRoofMm { get; set; } = 1800;

        /// <summary>
        /// How much of the roof edge (along its length/tangent direction) is visible in the
        /// section, in millimeters. Used both as the crop width along the edge tangent and
        /// as the camera's far-clip depth (single value serves both roles).
        /// </summary>
        public double SectionFarLengthMm { get; set; } = 1000;

        /// <summary>
        /// How far the crop extends OUTWARD from the roof edge, away from the roof
        /// (opposite direction from "into the roof"), in millimeters — e.g. to show the
        /// eave/fascia/gutter beyond the wall line.
        /// </summary>
        public double MarginOutwardMm { get; set; } = 300;

        /// <summary>
        /// How far the crop extends INWARD from the roof edge, into the roof toward the
        /// wall/structure below, in millimeters. Fixed value — no automatic wall search.
        /// </summary>
        public double LengthInsideRoofMm { get; set; } = 1200;

        /// <summary>Name of the View Template to apply to created sections, or "None".</summary>
        public string ViewTemplateName { get; set; } = "None";

        /// <summary>Post-creation behavior: "AskMe", "OpenAll", or "DontOpen".</summary>
        public string OpenViewsMode { get; set; } = "AskMe";

        /// <summary>
        /// When true, planned sections whose edge midpoints fall within MergeDistanceMm of an
        /// already-kept section are discarded (first-found kept). Applies across all selected
        /// roofs, not just within a single roof.
        /// </summary>
        public bool MergeEnabled { get; set; } = true;

        /// <summary>Proximity-merge threshold, in millimeters, measured between edge midpoints.</summary>
        public double MergeDistanceMm { get; set; } = 500;

        /// <summary>
        /// Ordered, toggleable tokens used to build each section view name.
        /// See <see cref="NamingToken"/> for the token set and default order.
        /// </summary>
        public List<NamingToken> NamingTokens { get; set; } = NamingToken.Defaults();

        /// <summary>Separator character(s) inserted between enabled naming tokens.</summary>
        public string NamingSeparator { get; set; } = "_";

        /// <summary>Last folder used for manual log export, reused for the session.</summary>
        public string LastLogExportFolder { get; set; } = "";
    }
}
