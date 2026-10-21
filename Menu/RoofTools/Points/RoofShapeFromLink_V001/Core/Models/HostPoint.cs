using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Models
{
    /// <summary>A shape-editing point read from a host roof (Revit internal units, feet).</summary>
    public sealed class HostPoint
    {
        /// <summary>Host roof element id.</summary>
        public ElementId RoofId { get; init; }

        /// <summary>Host roof label, "Type name [id]".</summary>
        public string RoofLabel { get; init; } = "";

        /// <summary>1-based point number within its roof.</summary>
        public int Index { get; init; }

        /// <summary>Position in the host model.</summary>
        public XYZ Position { get; init; }
    }

    /// <summary>The elevations found in the linked models for one host point (feet, host coordinates).</summary>
    public sealed class LinkedCandidate
    {
        /// <summary>Linked roof label, "Link name · roof id".</summary>
        public string LinkedRoof { get; init; } = "";

        /// <summary>Z of the linked roof's top face at the point's X,Y, or null.</summary>
        public double? SurfaceZ { get; init; }

        /// <summary>Z of the nearest shape-editing point of the linked roof, or null.</summary>
        public double? NearestZ { get; init; }

        /// <summary>Horizontal distance to that nearest point, in feet, or null.</summary>
        public double? NearestDistanceFt { get; init; }

        /// <summary>How the candidate was found.</summary>
        public string Note { get; init; } = "";
    }
}
