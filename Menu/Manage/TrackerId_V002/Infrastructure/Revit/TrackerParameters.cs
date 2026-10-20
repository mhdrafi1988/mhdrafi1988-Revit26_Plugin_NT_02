using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>
    /// The five shared parameters the tool writes. Names and GUIDs are fixed for good: schedules,
    /// filters and the Power BI export depend on them, so they must never be renamed or re-issued.
    /// </summary>
    public static class TrackerParameters
    {
        /// <summary>The permanent element ID.</summary>
        public const string TrackerId = "Tracker_ID";

        /// <summary>Zone at the last run.</summary>
        public const string Zone = "Tracker_Zone";

        /// <summary>Level at the last run.</summary>
        public const string Level = "Tracker_Level";

        /// <summary>Grid at the last run.</summary>
        public const string Grid = "Tracker_Grid";

        /// <summary>UniqueId of the element the ID was issued to; differs on a copy.</summary>
        public const string SourceGuid = "Tracker_SourceGuid";

        /// <summary>Status from the team's status file (1.1.0), e.g. "Installed".</summary>
        public const string Status = "Tracker_Status";

        /// <summary>Progress 0–100 from the status file (1.1.0); a Number parameter so filters can compare it.</summary>
        public const string Progress = "Tracker_Progress";

        /// <summary>Date of the status row (1.1.0), yyyy-MM-dd.</summary>
        public const string StatusDate = "Tracker_StatusDate";

        /// <summary>The three status parameters, written by the status import.</summary>
        public static readonly IReadOnlyList<string> StatusNames = new[] { Status, Progress, StatusDate };

        /// <summary>Shared parameter group the definitions are created in.</summary>
        public const string GroupName = "Tracker";

        /// <summary>
        /// Name, GUID, description and data type (Number, else Text) of every parameter, Tracker_ID first.
        /// New parameters are only ever appended.
        /// </summary>
        public static readonly IReadOnlyList<(string Name, Guid Guid, string Description, bool IsNumber)> All = new[]
        {
            (TrackerId, new Guid("7afba1b3-3c36-40c4-b622-104fa374d700"), "Permanent tracking ID of this element. Written once by the Tracker ID tool; never change it by hand.", false),
            (Zone, new Guid("00d22852-1eff-4137-aadb-5f6b94757db4"), "Zone of this element at the last Tracker ID run.", false),
            (Level, new Guid("eaa79360-4694-4488-8594-17d487241e99"), "Level of this element at the last Tracker ID run.", false),
            (Grid, new Guid("4a010dbe-ac6c-4971-8955-22b12e4a0bea"), "Nearest grid crossing of this element at the last Tracker ID run.", false),
            (SourceGuid, new Guid("b306dc6d-1058-4ca6-ae0a-5b3e67ed40aa"), "UniqueId of the element the Tracker_ID was issued to. Used to detect copied elements.", false),
            (Status, new Guid("15cff867-a1c5-4af3-bc47-21ebcde0be7e"), "Material / progress status from the Tracker status file (e.g. Ordered, Delivered, Installed).", false),
            (Progress, new Guid("bdfb3eab-54a5-4895-bb1a-a54befce2835"), "Progress 0–100 from the Tracker status file.", true),
            (StatusDate, new Guid("f33f9a95-3856-4c9c-a56c-f640e5ce5134"), "Date of the status row in the Tracker status file (yyyy-MM-dd).", false)
        };
    }
}
