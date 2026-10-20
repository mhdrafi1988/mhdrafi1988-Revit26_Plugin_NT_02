using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit
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

        /// <summary>Shared parameter group the definitions are created in.</summary>
        public const string GroupName = "Tracker";

        /// <summary>Name, GUID and description of every parameter, Tracker_ID first.</summary>
        public static readonly IReadOnlyList<(string Name, Guid Guid, string Description)> All = new[]
        {
            (TrackerId, new Guid("7afba1b3-3c36-40c4-b622-104fa374d700"), "Permanent tracking ID of this element. Written once by the Tracker ID tool; never change it by hand."),
            (Zone, new Guid("00d22852-1eff-4137-aadb-5f6b94757db4"), "Zone of this element at the last Tracker ID run."),
            (Level, new Guid("eaa79360-4694-4488-8594-17d487241e99"), "Level of this element at the last Tracker ID run."),
            (Grid, new Guid("4a010dbe-ac6c-4971-8955-22b12e4a0bea"), "Nearest grid crossing of this element at the last Tracker ID run."),
            (SourceGuid, new Guid("b306dc6d-1058-4ca6-ae0a-5b3e67ed40aa"), "UniqueId of the element the Tracker_ID was issued to. Used to detect copied elements.")
        };
    }
}
