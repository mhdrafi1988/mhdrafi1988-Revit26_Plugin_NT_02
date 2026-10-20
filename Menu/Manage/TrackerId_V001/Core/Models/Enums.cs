namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>The seven building blocks of a Tracker ID. Their order in the ID is set by the user.</summary>
    public enum SegmentKind
    {
        Zone,
        Level,
        Type,
        Grid,
        Custom1,
        Custom2,
        Custom3
    }

    /// <summary>
    /// Where a segment takes its value from. Each <see cref="SegmentKind"/> offers its own subset;
    /// see <see cref="Services.SegmentCatalog"/>. Saved by name in the settings file, so members may be
    /// added but never renamed.
    /// </summary>
    public enum SegmentSource
    {
        // Zone
        ZoneParameterThenScopeBox,
        ScopeBox,
        ZoneParameter,

        // Level
        LevelShortCode,
        LevelName,
        LevelElevation,
        LevelParameter,

        // Type
        TypeMark,
        TypeName,
        FamilyName,

        // Grid
        GridCrossing,
        GridLetter,
        GridNumber,

        // Custom 1-3
        FixedText,
        ParameterValue,
        RunningNumber
    }

    /// <summary>What the tool does with one element when IDs are written.</summary>
    public enum PlanAction
    {
        /// <summary>Has its own Tracker_ID; kept as is.</summary>
        Keep,

        /// <summary>Has its own Tracker_ID, but its zone, level or grid changed since it was written. The ID is kept.</summary>
        KeepMoved,

        /// <summary>Has a Tracker_ID nobody else uses but no matching source GUID; kept, and the GUID is stamped.</summary>
        Adopt,

        /// <summary>Has no Tracker_ID; a new one is created.</summary>
        Create,

        /// <summary>Carries another element's Tracker_ID (copy-paste); a new one is created.</summary>
        Copy,

        /// <summary>The new ID would repeat one already in use; nothing is written until the format is fixed.</summary>
        Duplicate,

        /// <summary>Cannot be edited (owned by another user, or the parameter is read-only); skipped.</summary>
        Locked
    }

    /// <summary>How many elements of a category or type already have a Tracker_ID.</summary>
    public enum CoverageState
    {
        None,
        Partial,
        Complete
    }
}
