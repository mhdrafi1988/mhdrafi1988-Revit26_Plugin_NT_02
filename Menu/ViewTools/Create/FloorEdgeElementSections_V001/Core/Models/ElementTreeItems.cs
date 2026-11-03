using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Root node grouping Category items under a single linked model instance.
    /// Simplified from LinkedDetailLineGenerator_VA003's LinkTreeNode: no
    /// RepresentationGroup (Profile/Linear/Point) split — this tool browses
    /// every category present in the linked document as one flat tree.
    /// </summary>
    public partial class LinkTreeNode : ObservableObject
    {
        public long LinkInstanceId { get; set; }
        public string LinkDisplayName { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        public ObservableCollection<CategoryTreeItem> Categories { get; set; } = new();
    }

    /// <summary>
    /// Category-level node (e.g. "Floors", "Walls", "Structural Columns", or any other
    /// category present in the linked document — not a fixed whitelist).
    /// IsChecked is cosmetic-only (not a tri-state cascade), matching the existing
    /// codebase's current behavior — only leaf TypeTreeItem.IsChecked actually drives
    /// selection.
    /// </summary>
    public partial class CategoryTreeItem : ObservableObject
    {
        public string CategoryName { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private bool? _isChecked = false;

        public ObservableCollection<FamilyTreeItem> Families { get; set; } = new();
    }

    /// <summary>Family-level node (e.g. "Generic Floor", "Basic Wall").</summary>
    public partial class FamilyTreeItem : ObservableObject
    {
        public string FamilyName { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isExpanded = true;

        [ObservableProperty]
        private bool? _isChecked = false;

        public ObservableCollection<TypeTreeItem> Types { get; set; } = new();
    }

    /// <summary>
    /// Leaf Type node (e.g. "150mm Floor"). Checking this is what actually includes
    /// this type's instances as candidates for edge matching.
    /// </summary>
    public partial class TypeTreeItem : ObservableObject
    {
        public long TypeId { get; set; }
        public string TypeName { get; set; } = string.Empty;

        /// <summary>Number of placed instances of this type found in the linked document — the "size" column in the flat Elements-to-Match grid.</summary>
        public int InstanceCount { get; set; }

        [ObservableProperty]
        private bool _isChecked;
    }

    /// <summary>
    /// One flattened, gridable row for the "Elements to Match" DataGrid — Category/Family/Type
    /// spelled out per row instead of nested under an ItemsControl tree, so the grid can be
    /// sorted (by Type, Count/"size", etc.), searched, and filtered by category.
    /// IsChecked binds straight through to <see cref="Source"/> (the underlying tree leaf),
    /// so toggling a grid row's checkbox is exactly the same action as checking it in the tree.
    /// </summary>
    public class LinkedElementRow
    {
        public string LinkDisplayName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public int InstanceCount { get; set; }
        public TypeTreeItem Source { get; set; }
    }

    /// <summary>
    /// One row of the Category Filter popover — a category name with a checkbox. The grid
    /// shows rows whose category has IsChecked = true, or every category when none are
    /// checked (empty selection == no filter applied).
    /// </summary>
    public partial class CategoryFilterOption : ObservableObject
    {
        public string CategoryName { get; set; } = string.Empty;

        [ObservableProperty]
        private bool _isChecked;
    }
}
