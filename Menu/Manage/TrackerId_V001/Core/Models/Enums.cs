namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>The eight building blocks of a Tracker ID. Their order in the ID is set by the user.</summary>
    public enum SegmentKind
    {
        Zone,
        Level,
        Type,
        Grid,
        Custom1,
        Custom2,
        Custom3,

        /// <summary>Code of the Revit model the element is in (ARC, STR, MEP…), so IDs never clash across models.</summary>
        Model
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
        RunningNumber,

        // Model
        ModelAuto,
        ModelProjectParameter,
        ModelFileName
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

    /// <summary>Which elements the tool looks at (step 1).</summary>
    public enum ScopeMode
    {
        /// <summary>Every placed element of the ticked categories (the 1.0.0 behaviour).</summary>
        WholeModel,

        /// <summary>Only the elements selected in Revit when "Use current selection" was clicked.</summary>
        Selection,

        /// <summary>Only the elements visible in the active view.</summary>
        ActiveView
    }

    /// <summary>File format of the Power BI export.</summary>
    public enum ExportFormat
    {
        Csv,
        Excel
    }

    /// <summary>What the model health check found.</summary>
    public enum HealthIssueKind
    {
        /// <summary>Several elements share one Tracker_ID and none of them is clearly the original.</summary>
        DuplicateId,

        /// <summary>The element carries another element's Tracker_ID (copy-paste).</summary>
        Copy,

        /// <summary>The element is in a tracked category but has no Tracker_ID.</summary>
        MissingId,

        /// <summary>Zone, level or grid changed since the ID was written.</summary>
        Moved,

        /// <summary>The nearest grid crossing is further than the tolerance.</summary>
        FarFromGrid,

        /// <summary>The element has a Tracker_ID but no status in the status file.</summary>
        NoStatus,

        /// <summary>The status file lists a Tracker_ID that no element in the model has.</summary>
        IdNotInModel
    }

    /// <summary>What a status import does to one element.</summary>
    public enum StatusChange
    {
        /// <summary>Status, progress or date differ from the file; they are written.</summary>
        Set,

        /// <summary>Already matches the file.</summary>
        Unchanged,

        /// <summary>The file has no row for this ID; the element's status is left as it is.</summary>
        NoRow,

        /// <summary>Owned by another user or read-only; skipped.</summary>
        Locked
    }
}
