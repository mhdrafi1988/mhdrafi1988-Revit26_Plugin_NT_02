using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Models
{
    /// <summary>
    /// One plan region in the Plan Region Manager grid.
    /// </summary>
    public partial class PlanRegionRow : ObservableObject
    {
        /// <summary>Element id of the plan region.</summary>
        public ElementId Id { get; }

        /// <summary>Id of the plan view that owns the region.</summary>
        public ElementId ViewId { get; }

        /// <summary>Name of the owning plan view.</summary>
        public string ViewName { get; }

        /// <summary>Name of the owning view's associated level, or empty.</summary>
        public string LevelName { get; }

        /// <summary>The region's own view range as Revit shows it, or empty.</summary>
        public string ViewRange { get; }

        /// <summary>True when the region is hidden in its view (element hidden or its category hidden).</summary>
        public bool IsHidden { get; }

        /// <summary>True when the region is hidden individually (Hide in View ▸ Elements), so Unhide can reveal it.</summary>
        public bool IsElementHidden { get; }

        /// <summary>Why the region is hidden: element, category, or empty when visible.</summary>
        public string HiddenReason { get; }

        /// <summary>Why the region can never be changed (owned by another user, …); empty when it can.</summary>
        public string BlockReason { get; }

        /// <summary>True when <see cref="BlockReason"/> is set.</summary>
        public bool IsBlocked => !string.IsNullOrEmpty(BlockReason);

        /// <summary>Group header: Visible or Hidden.</summary>
        public string GroupName => IsHidden ? "Hidden" : "Visible";

        /// <summary>Sort key so the Visible group lists before the Hidden group.</summary>
        public int GroupOrder => IsHidden ? 1 : 0;

        /// <summary>Badge text: Visible, Hidden, Category hidden or Blocked.</summary>
        public string Status => IsBlocked ? "Blocked" : IsHidden ? (IsElementHidden ? "Hidden" : "Category hidden") : "Visible";

        /// <summary>Tooltip explaining the status.</summary>
        public string StatusTip => IsBlocked ? BlockReason
            : IsHidden ? HiddenReason
            : "Shown in its view.";

        /// <summary>Ticked for an action.</summary>
        [ObservableProperty] private bool isSelected;

        /// <summary>Creates a row.</summary>
        public PlanRegionRow(ElementId id, ElementId viewId, string viewName, string levelName, string viewRange,
                             bool isElementHidden, bool isCategoryHidden, string blockReason)
        {
            Id = id;
            ViewId = viewId;
            ViewName = viewName ?? string.Empty;
            LevelName = levelName ?? string.Empty;
            ViewRange = viewRange ?? string.Empty;
            IsElementHidden = isElementHidden;
            IsHidden = isElementHidden || isCategoryHidden;
            HiddenReason = isElementHidden
                ? "Hidden in this view (Hide in View ▸ Elements). Unhide shows it again."
                : isCategoryHidden
                    ? "The Plan Region category is hidden in this view (Visibility/Graphics or a view template). Unhide cannot change that."
                    : string.Empty;
            BlockReason = blockReason ?? string.Empty;
        }
    }
}
