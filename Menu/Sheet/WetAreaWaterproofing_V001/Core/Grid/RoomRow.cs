using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid
{
    /// <summary>One room as the grid, the planner and the report see it. Plain data.</summary>
    public sealed class RoomRow
    {
        /// <summary>Stable key: "&lt;SourceKey&gt;:&lt;Room UniqueId&gt;".</summary>
        public string RoomKey { get; set; }

        /// <summary>Room ElementId in its own model.</summary>
        public long RoomId { get; set; }

        /// <summary>"Host" or the link's name.</summary>
        public string Source { get; set; }

        /// <summary>Level name (the host level for linked rooms when one matches).</summary>
        public string Level { get; set; }

        /// <summary>Level elevation in host coordinates, mm. Orders the level buttons.</summary>
        public double LevelElevationMm { get; set; }

        /// <summary>Room number.</summary>
        public string Number { get; set; }

        /// <summary>Room name.</summary>
        public string Name { get; set; }

        /// <summary>Room area, m².</summary>
        public double AreaM2 { get; set; }

        /// <summary>Keyword that marked the room as wet; null when none did.</summary>
        public string Matched { get; set; }

        /// <summary>Slab text for the grid, e.g. "Floor · +3.150" or "No slab · level +0".</summary>
        public string SlabText { get; set; }

        /// <summary>Slab ElementId; 0 when none was found.</summary>
        public long SlabId { get; set; }

        /// <summary>Slab top elevation in host coordinates, mm; null when no slab was found.</summary>
        public double? SlabTopMm { get; set; }

        /// <summary>Room state.</summary>
        public RoomStatus Status { get; set; }

        /// <summary>One line explaining the status (repair done, area difference, owner).</summary>
        public string StatusDetail { get; set; }

        /// <summary>User who has the room or slab checked out; null when free.</summary>
        public string Owner { get; set; }

        /// <summary>True when a roof from an earlier run exists for this room, whatever its status.</summary>
        public bool HasExistingRoof { get; set; }

        /// <summary>Room phase name.</summary>
        public string Phase { get; set; }

        /// <summary>Room workset name; empty when the model is not workshared.</summary>
        public string Workset { get; set; }
    }
}
