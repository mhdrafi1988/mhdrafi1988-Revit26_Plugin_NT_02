using System.Collections.Generic;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// One row of the Planned Sections grid: one detected cluster of linked elements
    /// near one floor edge, with its computed geometry and creation status.
    /// Adapted from FloorEdgeAroundSections_V004's PlannedSection — a row here only
    /// ever exists when at least one linked element matched a (floor, direction) pair
    /// (no "NoEdgeFound" placeholder rows, unlike V004).
    /// </summary>
    public partial class PlannedSection : ObservableObject
    {
        [ObservableProperty]
        private bool isIncluded;

        /// <summary>The source floor element id.</summary>
        public ElementId FloorId { get; set; }

        /// <summary>
        /// The source floor element itself, kept so the naming pass can resolve
        /// Zone/Level/Area tokens without re-collecting the floor from FloorId.
        /// </summary>
        public Floor FloorElement { get; set; }

        /// <summary>Display name of the floor (Name parameter, or "Floor_{ElementId}" fallback).</summary>
        public string FloorDisplayName { get; set; }

        /// <summary>View-aligned bucket this edge represents.</summary>
        public EdgeDirection Direction { get; set; }

        /// <summary>Proposed section view name, built from the configured naming pattern.</summary>
        public string SectionViewName { get; set; }

        /// <summary>Category display name of the matched linked-element cluster (e.g. "Walls").</summary>
        public string MatchedCategoryName { get; set; }

        /// <summary>
        /// The matched linked elements' identity — elements live in linked documents, so a
        /// plain ElementId isn't enough; each pair is (LinkInstanceId, ElementId).
        /// </summary>
        public List<(long LinkInstanceId, long ElementId)> MatchedElementRefs { get; set; } = new();

        /// <summary>Number of linked elements in this cluster.</summary>
        public int MatchedElementCount { get; set; }

        /// <summary>The actual boundary curve selected for this direction.</summary>
        public Curve EdgeCurve { get; set; }

        /// <summary>The cluster's representative point, projected onto EdgeCurve (not the full edge midpoint).</summary>
        public XYZ EdgeMidpoint { get; set; }

        /// <summary>Inward-facing normal at the edge, used as the section's cut/search axis.</summary>
        public XYZ InwardNormal { get; set; }

        /// <summary>Floor's own bounding box, used for crop sizing.</summary>
        public BoundingBoxXYZ FloorBoundingBox { get; set; }

        /// <summary>Result of the plan-build pass: Ready / ClusterCapped.</summary>
        public PlannedSectionStatus Status { get; set; }

        /// <summary>
        /// When Status == ClusterCapped, a human-readable note on why this cluster was
        /// dropped (for the status column / log traceability). Null otherwise.
        /// </summary>
        public string MergedIntoDescription { get; set; }

        /// <summary>Human-readable status text bound to the grid's Status column.</summary>
        public string StatusText => Status switch
        {
            PlannedSectionStatus.Ready => "Ready",
            PlannedSectionStatus.ClusterCapped => string.IsNullOrEmpty(MergedIntoDescription)
                ? "Capped"
                : $"Capped — {MergedIntoDescription}",
            _ => "Unknown"
        };

        public PlannedSection()
        {
            isIncluded = true;
        }
    }
}
