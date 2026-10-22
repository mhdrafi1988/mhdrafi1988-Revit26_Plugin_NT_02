namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models
{
    /// <summary>
    /// Groups Revit ViewType values into the buckets that actually share a
    /// name-uniqueness namespace in Revit. Used for:
    ///   1. Duplicate-name detection/fix (per-group, not global).
    ///   2. The View Type popover filter's category headers.
    ///
    /// IMPORTANT: Section and Callout share ONE group because Revit enforces
    /// name-uniqueness across both together (a Callout and a Section cannot
    /// share a name) — this matches Revit's actual behavior, not an
    /// arbitrary UI grouping.
    ///
    /// Each other family (FloorPlan, CeilingPlan, StructuralPlan, AreaPlan,
    /// Elevation, Drafting, Legend, Schedule) is its own separate group
    /// because Revit only enforces uniqueness within that exact view type.
    /// </summary>
    public enum ViewTypeGroup
    {
        SectionOrCallout,
        FloorPlan,
        CeilingPlan,
        StructuralPlan,
        AreaPlan,
        Elevation,
        Drafting,
        Legend,
        Schedule
    }
}
