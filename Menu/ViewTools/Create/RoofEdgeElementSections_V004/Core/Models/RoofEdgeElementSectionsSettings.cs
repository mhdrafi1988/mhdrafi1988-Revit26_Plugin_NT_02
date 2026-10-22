using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V004
{
    /// <summary>
    /// Persisted user settings for RoofEdgeElementSections V002.
    /// Serialized to %AppData%\Revit26_Plugin_Revised\RoofEdgeElementSections_V002\settings.json
    /// via System.Text.Json. Loaded on window open, saved on window close and after Run.
    ///
    /// Unlike RoofEdgeAroundSections_V004's fixed proximity-merge threshold, this tool
    /// uses two distinct tolerances (per confirmed spec): how close a linked element must
    /// be to a roof edge to "count" for that side, and how close two same-category
    /// elements must be to each other to collapse into one section.
    /// </summary>
    public class RoofEdgeElementSectionsSettings
    {
        /// <summary>
        /// Max distance from a linked element to a bucketed roof edge curve for that
        /// element to "count" as being near that side, in millimeters.
        /// </summary>
        public double EdgeProximityToleranceMm { get; set; } = 300;

        /// <summary>
        /// Max distance (measured along the edge curve's parameterization) between two
        /// same-category matched elements for them to collapse into one section cluster,
        /// in millimeters.
        /// </summary>
        public double SameSideDedupToleranceMm { get; set; } = 600;

        /// <summary>
        /// How far the crop extends OUTWARD from the roof edge, away from the roof
        /// (opposite direction from "into the roof"), in millimeters.
        /// </summary>
        public double MarginOutwardMm { get; set; } = 300;

        /// <summary>How far below the roof edge's own elevation the crop reaches, in millimeters.</summary>
        public double BelowRoofMm { get; set; } = 600;

        /// <summary>How far above the roof edge's own elevation the crop reaches, in millimeters.</summary>
        public double AboveRoofMm { get; set; } = 1800;

        /// <summary>
        /// How far the crop extends INWARD from the roof edge, into the roof toward the
        /// wall/structure below, in millimeters. Fixed value — no automatic wall search.
        /// </summary>
        public double LengthInsideRoofMm { get; set; } = 1200;

        /// <summary>
        /// How much of the roof edge (along its length/tangent direction) is visible in the
        /// section, in millimeters. Used both as the crop width along the edge tangent and
        /// as the camera's far-clip depth.
        /// </summary>
        public double FarClipMm { get; set; } = 1000;

        /// <summary>Name of the View Template to apply to created sections, or "None".</summary>
        public string ViewTemplateName { get; set; } = "None";

        /// <summary>Post-creation behavior: "AskMe", "OpenAll", or "DontOpen".</summary>
        public string OpenViewsMode { get; set; } = "AskMe";

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
