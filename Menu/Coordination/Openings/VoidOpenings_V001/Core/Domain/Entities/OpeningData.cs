using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>The opening calculated for one clash. Lengths are millimetres.</summary>
    public sealed class OpeningData
    {
        /// <summary>The clash this opening answers.</summary>
        public ClashData Clash { get; set; }

        /// <summary>Id of the clash.</summary>
        public string ClashId => Clash?.ClashId;

        /// <summary>Job ID; null until the opening is generated.</summary>
        public string JobId { get; set; }

        /// <summary>Centre of the opening, mid-depth in the structure.</summary>
        public Vec3 Center => Orientation.Origin;

        /// <summary>Position and orientation of the opening.</summary>
        public Frame3 Orientation { get; set; }

        /// <summary>Circular or rectangular.</summary>
        public OpeningShape Shape { get; set; }

        /// <summary>Width of a rectangular opening (along <see cref="Frame3.AxisX"/>); 0 for a circular one.</summary>
        public double WidthMm { get; set; }

        /// <summary>Height of a rectangular opening (along <see cref="Frame3.AxisY"/>); 0 for a circular one.</summary>
        public double HeightMm { get; set; }

        /// <summary>Diameter of a circular opening; 0 for a rectangular one.</summary>
        public double DiameterMm { get; set; }

        /// <summary>Length of the void along <see cref="Frame3.AxisZ"/>, overshoot included.</summary>
        public double DepthMm { get; set; }

        /// <summary>The clearance value that was applied (per side or total, as the rule says).</summary>
        public double ClearanceMm { get; set; }

        /// <summary>Workflow status.</summary>
        public OpeningStatus Status { get; set; }

        /// <summary>Relation to records stored by earlier runs.</summary>
        public TrackingState Tracking { get; set; }

        /// <summary>Why the opening needs a review; empty when it does not.</summary>
        public string ReviewNote { get; set; } = string.Empty;
    }
}
