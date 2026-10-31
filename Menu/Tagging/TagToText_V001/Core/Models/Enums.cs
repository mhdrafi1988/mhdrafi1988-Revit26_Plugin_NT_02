namespace Revit26_Plugin_Revised.TagToText.V001.Core.Models
{
    /// <summary>The kinds of annotation Tag To Text can copy.</summary>
    public enum TagKind
    {
        /// <summary>Spot Elevation (a <c>SpotDimension</c> in OST_SpotElevations).</summary>
        SpotElevation,

        /// <summary>Spot Coordinate (OST_SpotCoordinates). Copied as two notes: N and E.</summary>
        SpotCoordinate,

        /// <summary>Spot Slope (OST_SpotSlopes).</summary>
        SpotSlope,

        /// <summary>Any <c>IndependentTag</c>: roof, door, window, keynote, multi-category…</summary>
        ElementTag,

        /// <summary>Room, Area or Space tag (<c>SpatialElementTag</c>).</summary>
        SpatialTag
    }

    /// <summary>
    /// What one level of the tag table's grouping groups by (see <c>TagToTextSettings.GroupLevel1</c>).
    /// Values are stored by number in settings.json: never renumber them.
    /// </summary>
    public enum GroupLevel
    {
        /// <summary>No grouping at this level.</summary>
        None = 0,

        /// <summary>The table's Type column: Spot Elevation, Spot Coordinate, Spot Slope, or the tag category.</summary>
        Kind = 1,

        /// <summary>The Revit category of the tag, e.g. "Roof Tags" or "Spot Elevations".</summary>
        Category = 2,

        /// <summary>The tag's family and type, e.g. "Roof Tag : Slope 2%".</summary>
        TagType = 3,

        /// <summary>The category of the element the tag points at, e.g. "Roofs" or "Rooms".</summary>
        TaggedCategory = 4,

        /// <summary>Whether the tag can be copied: OK, no text, in a group, owned by another user, problem.</summary>
        Status = 5,

        /// <summary>Tags that show identical text.</summary>
        SameText = 6
    }

    /// <summary>Which tags the window lists.</summary>
    public enum SourceMode
    {
        /// <summary>The tags selected in Revit (or picked with Pick Tags).</summary>
        Selection,

        /// <summary>Every supported tag in the active view.</summary>
        ActiveView
    }

    /// <summary>What happens to the original tags after their text is copied.</summary>
    public enum OriginalTagAction
    {
        /// <summary>Leave them as they are (default).</summary>
        Keep,

        /// <summary>Hide them in the view.</summary>
        Hide,

        /// <summary>Delete them.</summary>
        Delete
    }

    /// <summary>Where the new text notes go.</summary>
    public enum TargetMode
    {
        /// <summary>The view the tags are in.</summary>
        SameView,

        /// <summary>A new duplicate of that view.</summary>
        NewView
    }

    /// <summary>How the new view is duplicated.</summary>
    public enum DuplicateMode
    {
        /// <summary>Duplicate without detailing (no tags, text or detail items are copied).</summary>
        WithoutDetailing,

        /// <summary>Duplicate with detailing (annotation is copied too).</summary>
        WithDetailing
    }
}
