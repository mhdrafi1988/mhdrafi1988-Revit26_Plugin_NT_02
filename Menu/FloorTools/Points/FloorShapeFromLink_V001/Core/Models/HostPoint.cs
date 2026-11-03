using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Models
{
    /// <summary>
    /// The host floor's base surface elevation and default thickness (Revit internal units, feet).
    /// </summary>
    public sealed class HostFloor
    {
        /// <summary>Base level elevation plus the floor's Base Offset From Level, in host coordinates.</summary>
        public double BaseZFt { get; init; }

        /// <summary>Total thickness of the host floor type (all layers).</summary>
        public double ThicknessFt { get; init; }

        /// <summary>Problem reading the level or thickness (empty when both were read).</summary>
        public string Note { get; init; } = "";
    }

    /// <summary>A shape-editing point read from a host floor (Revit internal units, feet).</summary>
    public sealed class HostPoint
    {
        /// <summary>Host floor element id.</summary>
        public ElementId FloorId { get; init; }

        /// <summary>Host floor label, "Type name [id]".</summary>
        public string FloorLabel { get; init; } = "";

        /// <summary>1-based point number within its floor.</summary>
        public int Index { get; init; }

        /// <summary>Position in the host model.</summary>
        public XYZ Position { get; init; }

        /// <summary>Base surface elevation and thickness of the floor this point belongs to.</summary>
        public HostFloor Base { get; init; }
    }

    /// <summary>The elevations found in the linked models for one host point (feet, host coordinates).</summary>
    public sealed class LinkedCandidate
    {
        /// <summary>Linked floor label, "Link name · floor id".</summary>
        public string LinkedFloor { get; init; } = "";

        /// <summary>Z of the linked floor's top face at the point's X,Y, or null.</summary>
        public double? SurfaceZ { get; init; }

        /// <summary>Z of the nearest shape-editing point of the linked floor, or null.</summary>
        public double? NearestZ { get; init; }

        /// <summary>Horizontal distance to that nearest point, in feet, or null.</summary>
        public double? NearestDistanceFt { get; init; }

        /// <summary>How the candidate was found.</summary>
        public string Note { get; init; } = "";
    }
}
