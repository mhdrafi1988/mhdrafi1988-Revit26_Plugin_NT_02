using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models
{
    /// <summary>
    /// V026 NEW. One row in the UI's Priority Groups list — a ViewType
    /// present among the views on the active sheet, plus the user's chosen
    /// placement rank for it (the row's index within
    /// SheetAutoRearrangeViewModel.PriorityGroups). When priority grouping
    /// is enabled, every view of the 1st-ranked ViewType is packed before
    /// any view of the 2nd-ranked ViewType, and so on — see
    /// SheetOrderPackingService.BuildPriorityGroups.
    /// </summary>
    public partial class PriorityGroupRow : ObservableObject
    {
        public ViewType ViewType { get; }

        /// <summary>Human-friendly plural label for the grid (e.g. "Sections", "Drafting Views") — see SheetAutoRearrangeViewModel.FriendlyViewTypeName.</summary>
        public string DisplayName { get; }

        public PriorityGroupRow(ViewType viewType, string displayName)
        {
            ViewType = viewType;
            DisplayName = displayName;
        }
    }
}
