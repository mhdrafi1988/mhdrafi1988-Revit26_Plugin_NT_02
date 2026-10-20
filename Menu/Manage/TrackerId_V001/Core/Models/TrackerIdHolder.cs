namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>
    /// One element anywhere in the model that has a Tracker_ID. The planner uses the full list to keep
    /// new IDs unique across the whole model and to tell an original from its copies.
    /// </summary>
    public sealed class TrackerIdHolder
    {
        /// <summary>Creates the record.</summary>
        public TrackerIdHolder(long elementId, string uniqueId, string trackerId, string sourceGuid)
        {
            ElementId = elementId;
            UniqueId = uniqueId ?? string.Empty;
            TrackerId = trackerId ?? string.Empty;
            SourceGuid = sourceGuid ?? string.Empty;
        }

        /// <summary>Revit element id.</summary>
        public long ElementId { get; }

        /// <summary>Revit UniqueId.</summary>
        public string UniqueId { get; }

        /// <summary>The element's Tracker_ID.</summary>
        public string TrackerId { get; }

        /// <summary>The element's Tracker_SourceGuid, empty when none.</summary>
        public string SourceGuid { get; }

        /// <summary>True when the stored source GUID is this element's own UniqueId, i.e. it is the original.</summary>
        public bool IsOriginal => SourceGuid.Length > 0 && SourceGuid == UniqueId;
    }
}
