namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models
{
    /// <summary>Where a visible element comes from: the host model, or a loaded Revit link.</summary>
    public enum ElementSourceKind
    {
        /// <summary>An element of the document the tool runs in.</summary>
        Host,

        /// <summary>An element of a loaded <see cref="Autodesk.Revit.DB.RevitLinkInstance"/>.</summary>
        Link
    }

    /// <summary>
    /// What a listed element type may be used for. Only roofs and walls draw lines;
    /// every other category is listed so the section's contents can be read and
    /// exported, but cannot be ticked.
    /// </summary>
    public enum ElementRole
    {
        /// <summary>Listed for information only — no lines are drawn from it.</summary>
        Listed,

        /// <summary>Roofs: the whole cut profile is traced.</summary>
        Roof,

        /// <summary>Walls: one vertical line over the wall's visible height, when the wall is
        /// adjacent to a ticked roof's cut profile.</summary>
        Wall
    }

    /// <summary>How the sections to run over were chosen, for the status line.</summary>
    public enum SectionSourceKind
    {
        /// <summary>Every section view placed on the active sheet.</summary>
        Sheet,

        /// <summary>The active section view alone, because it is not on a sheet.</summary>
        ActiveViewOnly
    }
}
