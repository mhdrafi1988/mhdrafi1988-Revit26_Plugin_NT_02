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

        /// <summary>Schema version of this file. 1 = the format this release writes.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Line style for the roof cut profile. Empty = the view's default line style.</summary>
        public string RoofLineStyleName { get; set; } = string.Empty;

        /// <summary>Line style for the wall lines. Empty = the view's default line style.</summary>
        public string WallLineStyleName { get; set; } = string.Empty;

        /// <summary>Draw the cut profile of every ticked roof.</summary>
        public bool DrawRoofProfile { get; set; } = true;

        /// <summary>Draw a vertical line on walls adjacent to a ticked roof's cut profile.</summary>
        public bool DrawWallLines { get; set; } = true;

        /// <summary>How close a wall's cut profile must come to a roof's cut profile to count as
        /// adjacent, in millimetres.</summary>
        public double AdjacencyToleranceMm { get; set; } = 50.0;

        /// <summary>Draw the wall line on both faces of an adjacent wall instead of only the face
        /// nearer the roof.</summary>
        public bool WallLineOnBothFaces { get; set; }

        /// <summary>Leave out a line that matches one already in the view (same style, same ends
        /// within tolerance), so a second run does not stack duplicates.</summary>
        public bool SkipExistingLines { get; set; } = true;

        /// <summary>Show every category in the element list, not only roofs and walls.</summary>
        public bool ShowAllCategories { get; set; } = true;

        /// <summary>
        /// Returns this instance with every value inside its allowed range — call right after
        /// loading. Never returns null.
        /// </summary>
        public SectionRoofLinesSettings Normalize()
        {
            if (Version < 1) Version = 1;
            RoofLineStyleName ??= string.Empty;
            WallLineStyleName ??= string.Empty;
            if (double.IsNaN(AdjacencyToleranceMm) || double.IsInfinity(AdjacencyToleranceMm))
                AdjacencyToleranceMm = 50.0;
            AdjacencyToleranceMm = Math.Min(1000.0, Math.Max(0.1, AdjacencyToleranceMm));
            if (!DrawRoofProfile && !DrawWallLines) DrawRoofProfile = true;
            return this;
        }
    }
}
