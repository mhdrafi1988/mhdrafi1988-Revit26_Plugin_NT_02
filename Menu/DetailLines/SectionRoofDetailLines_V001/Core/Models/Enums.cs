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

    /// <summary>
    /// Which side of a cut profile an edge is on, read from the side the material lies on rather
    /// than from where the edge sits in the profile — so the underside of a roof is the bottom
    /// whether the roof is flat, sloped or stepped, and the head of an opening the section passes
    /// through is a bottom edge too, because that is the face you see from below.
    /// </summary>
    public enum ProfileSide
    {
        /// <summary>The edge faces up: the top surface of the element.</summary>
        Top,

        /// <summary>The edge faces down: the underside, or the soffit over an opening.</summary>
        Bottom,

        /// <summary>The edge faces left across the view.</summary>
        Left,

        /// <summary>The edge faces right across the view.</summary>
        Right
    }

    /// <summary>
    /// Which face of an adjacent wall gets a line. <see cref="NearerRoof"/> is the behaviour the
    /// tool shipped with in 1.0.0 and stays the default.
    /// </summary>
    public enum WallFaceChoice
    {
        /// <summary>Only the face nearer the ticked roof's cut profile.</summary>
        NearerRoof,

        /// <summary>Every face of the wall's cut profile.</summary>
        BothFaces,

        /// <summary>Only the leftmost face across the view.</summary>
        LeftOnly,

        /// <summary>Only the rightmost face across the view.</summary>
        RightOnly
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
