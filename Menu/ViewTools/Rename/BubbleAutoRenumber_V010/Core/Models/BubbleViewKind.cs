namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models
{
    /// <summary>
    /// What kind of view a viewport holds, as far as this tool's filtering cares.
    /// Deliberately NOT Autodesk's ViewType: Core carries no Revit API dependency
    /// (CLAUDE.md / ARCHITECTURE.md §3), so Infrastructure/Helpers/ViewportReader
    /// maps ViewType → BubbleViewKind on the way in.
    ///
    /// NEW in V009 — V008 recognised sections only, and cast to ViewSection, which
    /// a drafting view (ViewDrafting) can never be.
    /// </summary>
    public enum BubbleViewKind
    {
        /// <summary>ViewType.Section.</summary>
        Section,

        /// <summary>ViewType.DraftingView.</summary>
        Drafting,

        /// <summary>ViewType.Detail — a callout/detail view.</summary>
        Detail,

        /// <summary>ViewType.Elevation.</summary>
        Elevation,

        /// <summary>Anything else that still carries a detail number (plans, 3D, legends…).
        /// Only ever included when RenumberOptions.AnyNumberedView is on.</summary>
        Other
    }
}
