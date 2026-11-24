using System;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models
{
    /// <summary>
    /// What the window remembers between runs, saved to
    /// %AppData%\Revit26_Plugin_Revised\SectionRoofDetailLines\settings.json.
    /// <para>
    /// <see cref="Version"/> is written so a later release can read an older file and migrate it;
    /// <see cref="Normalize"/> brings any out-of-range or missing value back to a usable default,
    /// so a hand-edited or older file never stops the tool from opening.
    /// </para>
    /// </summary>
    public sealed class SectionRoofLinesSettings
    {
        /// <summary>Largest number of sections one run covers. The sheet's sections are capped
        /// at this when none are ticked by hand.</summary>
        public const int MaxSectionsPerRun = 10;

        /// <summary>
        /// Schema version of this file. 1 = what tool 1.0.0 wrote; 2 = adds the per-side roof
        /// choices and <see cref="WallFace"/>. A version-1 file is read and migrated by
        /// <see cref="Normalize"/>, never rejected.
        /// </summary>
        public int Version { get; set; } = 2;

        /// <summary>Line style for the roof cut profile. Empty = the view's default line style.</summary>
        public string RoofLineStyleName { get; set; } = string.Empty;

        /// <summary>Line style for the wall lines. Empty = the view's default line style.</summary>
        public string WallLineStyleName { get; set; } = string.Empty;

        /// <summary>Draw the cut profile of every ticked roof.</summary>
        public bool DrawRoofProfile { get; set; } = true;

        /// <summary>Draw the edges of a roof's cut profile that face up — its top surface.</summary>
        public bool RoofTop { get; set; } = true;

        /// <summary>Draw the edges that face down — the underside, and the soffit over any opening
        /// the section passes through.</summary>
        public bool RoofBottom { get; set; } = true;

        /// <summary>Draw the edges that face left across the view.</summary>
        public bool RoofLeft { get; set; } = true;

        /// <summary>Draw the edges that face right across the view.</summary>
        public bool RoofRight { get; set; } = true;

        /// <summary>Draw a vertical line on walls adjacent to a ticked roof's cut profile.</summary>
        public bool DrawWallLines { get; set; } = true;

        /// <summary>How close a wall's cut profile must come to a roof's cut profile to count as
        /// adjacent, in millimetres.</summary>
        public double AdjacencyToleranceMm { get; set; } = 50.0;

        /// <summary>Which face of an adjacent wall gets a line.</summary>
        public WallFaceChoice WallFace { get; set; } = WallFaceChoice.NearerRoof;

        /// <summary>
        /// Version-1 name for "both faces". Kept so a settings file written by tool 1.0.0 still
        /// reads, and migrated into <see cref="WallFace"/> by <see cref="Normalize"/>; nothing in
        /// the tool reads it after that.
        /// </summary>
        public bool WallLineOnBothFaces { get; set; }

        /// <summary>Leave out a line that matches one already in the view (same style, same ends
        /// within tolerance), so a second run does not stack duplicates.</summary>
        public bool SkipExistingLines { get; set; } = true;

        /// <summary>Show every category in the element list, not only roofs and walls.</summary>
        public bool ShowAllCategories { get; set; } = true;

        /// <summary>The sides of a roof's cut profile to draw, as a set.</summary>
        public System.Collections.Generic.HashSet<ProfileSide> RoofSides()
        {
            var sides = new System.Collections.Generic.HashSet<ProfileSide>();
            if (RoofTop) sides.Add(ProfileSide.Top);
            if (RoofBottom) sides.Add(ProfileSide.Bottom);
            if (RoofLeft) sides.Add(ProfileSide.Left);
            if (RoofRight) sides.Add(ProfileSide.Right);
            return sides;
        }

        /// <summary>
        /// Returns this instance with every value inside its allowed range — call right after
        /// loading. Never returns null.
        /// </summary>
        public SectionRoofLinesSettings Normalize()
        {
            // A file from tool 1.0.0 has no side choices and no WallFace: its "both faces" flag is
            // what carried that decision, so it is read once and carried over. Afterwards the file
            // is version 2 and the flag is ignored.
            if (Version < 2)
            {
                if (WallLineOnBothFaces) WallFace = WallFaceChoice.BothFaces;
                Version = 2;
            }

            RoofLineStyleName ??= string.Empty;
            WallLineStyleName ??= string.Empty;
            if (double.IsNaN(AdjacencyToleranceMm) || double.IsInfinity(AdjacencyToleranceMm))
                AdjacencyToleranceMm = 50.0;
            AdjacencyToleranceMm = Math.Min(1000.0, Math.Max(0.1, AdjacencyToleranceMm));
            if (!DrawRoofProfile && !DrawWallLines) DrawRoofProfile = true;

            // Drawing the roof profile with no side chosen would create nothing and say nothing,
            // so an empty choice comes back as the whole profile — what 1.0.0 drew.
            if (DrawRoofProfile && !RoofTop && !RoofBottom && !RoofLeft && !RoofRight)
                RoofTop = RoofBottom = RoofLeft = RoofRight = true;

            if (!System.Enum.IsDefined(typeof(WallFaceChoice), WallFace))
                WallFace = WallFaceChoice.NearerRoof;

            WallLineOnBothFaces = WallFace == WallFaceChoice.BothFaces;
            return this;
        }
    }
}
