using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Persisted user settings for FloorEdgeElementSections V002.
    /// Serialized to %AppData%\Revit26_Plugin_Revised\FloorEdgeElementSections\settings.json
    /// via System.Text.Json. Loaded on window open, saved on window close and after Run.
    ///
    /// Unlike FloorEdgeAroundSections_V004's fixed proximity-merge threshold, this tool
    /// uses two distinct tolerances (per confirmed spec): how close a linked element must
    /// be to a floor edge to "count" for that side, and how close two same-category
    /// elements must be to each other to collapse into one section.
    /// </summary>
    public class FloorEdgeElementSectionsSettings
    {
        /// <summary>
        /// Max distance from a linked element to a bucketed floor edge curve for that
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
        /// How far the crop extends OUTWARD from the floor edge, away from the floor
        /// (opposite direction from "into the floor"), in millimeters.
        /// </summary>
        public double MarginOutwardMm { get; set; } = 300;

        /// <summary>How far below the floor edge's own elevation the crop reaches, in millimeters.</summary>
        public double BelowFloorMm { get; set; } = 600;

        /// <summary>How far above the floor edge's own elevation the crop reaches, in millimeters.</summary>
        public double AboveFloorMm { get; set; } = 1800;

        /// <summary>
        /// How far the crop extends INWARD from the floor edge, into the floor toward the
        /// wall/structure below, in millimeters. Fixed value — no automatic wall search.
        /// </summary>
        public double LengthInsideFloorMm { get; set; } = 1200;

        /// <summary>
        /// How much of the floor edge (along its length/tangent direction) is visible in the
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
