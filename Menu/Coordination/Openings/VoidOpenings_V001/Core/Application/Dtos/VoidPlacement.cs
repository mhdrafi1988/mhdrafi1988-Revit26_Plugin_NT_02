using System.Collections.Generic;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos
{
    /// <summary>
    /// One opening ready to be placed: geometry in the coordinates of the model it will be placed in
    /// (millimetres), plus what it belongs to. Used for Generate and for the opening package, so it is
    /// flat and serialises as is. Add fields, never rename them.
    /// </summary>
    public sealed class VoidPlacement
    {
        /// <summary>The clash key as text.</summary>
        public string ClashKey { get; set; }

        /// <summary>Display id of the clash.</summary>
        public string ClashId { get; set; }

        /// <summary>"Circular" or "Rectangular".</summary>
        public string Shape { get; set; }

        /// <summary>Centre X.</summary>
        public double CenterX { get; set; }

        /// <summary>Centre Y.</summary>
        public double CenterY { get; set; }

        /// <summary>Centre Z.</summary>
        public double CenterZ { get; set; }

        /// <summary>Cut direction, X component.</summary>
        public double AxisX { get; set; }

        /// <summary>Cut direction, Y component.</summary>
        public double AxisY { get; set; }

        /// <summary>Cut direction, Z component.</summary>
        public double AxisZ { get; set; }

        /// <summary>Width direction, X component.</summary>
        public double WidthDirX { get; set; }

        /// <summary>Width direction, Y component.</summary>
        public double WidthDirY { get; set; }

        /// <summary>Width direction, Z component.</summary>
        public double WidthDirZ { get; set; }

        /// <summary>Width in millimetres (rectangular).</summary>
        public double WidthMm { get; set; }

        /// <summary>Height in millimetres (rectangular).</summary>
        public double HeightMm { get; set; }

        /// <summary>Diameter in millimetres (circular).</summary>
        public double DiameterMm { get; set; }

        /// <summary>Depth in millimetres.</summary>
        public double DepthMm { get; set; }

        /// <summary>Applied clearance in millimetres.</summary>
        public double ClearanceMm { get; set; }

        /// <summary>Key of the structural model.</summary>
        public string StructuralModelKey { get; set; }

        /// <summary>Name of the structural model.</summary>
        public string StructuralModelName { get; set; }

        /// <summary>UniqueId of the structural element to cut.</summary>
        public string StructuralUniqueId { get; set; }

        /// <summary>Element id of the structural element.</summary>
        public long StructuralElementId { get; set; }

        /// <summary>Structural category (enum name).</summary>
        public string StructuralCategory { get; set; }

        /// <summary>Key of the MEP model.</summary>
        public string MepModelKey { get; set; }

        /// <summary>Name of the MEP model.</summary>
        public string MepModelName { get; set; }

        /// <summary>UniqueId of the MEP element.</summary>
        public string MepUniqueId { get; set; }

        /// <summary>Element id of the MEP element.</summary>
        public long MepElementId { get; set; }

        /// <summary>MEP category (enum name).</summary>
        public string MepCategory { get; set; }
    }

    /// <summary>What happened to one placement.</summary>
    public sealed class PlacementOutcome
    {
        /// <summary>The clash key of the placement.</summary>
        public string ClashKey { get; set; }

        /// <summary>Job ID given to (or kept by) the opening; null when placing failed.</summary>
        public string JobId { get; set; }

        /// <summary>UniqueId of the void instance; null when placing failed.</summary>
        public string InstanceUniqueId { get; set; }

        /// <summary>"Cut", "Generated", "Existing" or "Failed".</summary>
        public string Status { get; set; }

        /// <summary>Reason for a failure or for not cutting; empty otherwise.</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Hand-over file for a linked structural model: openings in that model's own coordinates, to be
    /// applied by running the tool inside the structural file.
    /// </summary>
    public sealed class OpeningPackage
    {
        /// <summary>Version written by this build.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Format version of the file.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Key of the structural model the openings are for.</summary>
        public string StructuralModelKey { get; set; }

        /// <summary>Name of that model.</summary>
        public string StructuralModelName { get; set; }

        /// <summary>Key of the MEP model the openings come from.</summary>
        public string MepModelKey { get; set; }

        /// <summary>Name of that model.</summary>
        public string MepModelName { get; set; }

        /// <summary>When the package was written (UTC, ISO 8601).</summary>
        public string CreatedUtc { get; set; }

        /// <summary>The openings.</summary>
        public List<VoidPlacement> Openings { get; set; } = new List<VoidPlacement>();
    }
}
