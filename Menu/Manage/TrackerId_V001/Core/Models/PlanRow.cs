namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>The planner's decision for one element.</summary>
    public sealed class PlanRow
    {
        /// <summary>The element as read from the model.</summary>
        public ElementSnapshot Element { get; set; }

        /// <summary>What happens to the element.</summary>
        public PlanAction Action { get; set; }

        /// <summary>The Tracker_ID the element will have after writing (its existing one when kept).</summary>
        public string ResultId { get; set; } = string.Empty;

        /// <summary>Zone value for Tracker_Zone, from the Zone segment's source.</summary>
        public string Zone { get; set; } = string.Empty;

        /// <summary>Level value for Tracker_Level, from the Level segment's source.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Grid value for Tracker_Grid, from the Grid segment's source.</summary>
        public string Grid { get; set; } = string.Empty;

        /// <summary>Distance from the element's centre to that grid crossing or line, metres; null when unknown.</summary>
        public double? GridDistanceM { get; set; }

        /// <summary>True when <see cref="GridDistanceM"/> is over the format's tolerance.</summary>
        public bool GridCheck { get; set; }

        /// <summary>Short explanation shown in the grid's tooltip and the log.</summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>True when the element gets a new Tracker_ID.</summary>
        public bool GetsNewId => Action == PlanAction.Create || Action == PlanAction.Copy;
    }
}
