using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.UI.ViewModels
{
    /// <summary>
    /// One row of the Workset -> Category -> Type tree. Carries a tri-state
    /// checkbox: checking/unchecking a parent cascades to every descendant,
    /// and a child's change bubbles back up to recompute its ancestors'
    /// checked/unchecked/indeterminate state — same behavior as a Windows
    /// Explorer checkbox tree.
    /// </summary>
    public partial class TreeNodeViewModel : ObservableObject
    {
        public ElementTreeNodeKind Kind { get; }
        public string Name { get; }
        public int ElementCount { get; }
        public string Owner { get; }
        public string EditableBadge { get; }

        /// <summary>"open" or "closed" on Workset rows; empty on every other row.</summary>
        public string OpenBadge { get; }

        /// <summary>True on a Workset row whose workset is closed in this session.</summary>
        public bool IsClosed { get; }

        /// <summary>Only populated on Type nodes — the actual element instances this row represents.</summary>
        public IReadOnlyList<ElementId> ElementIds { get; }

        public ObservableCollection<TreeNodeViewModel> Children { get; } = new();
        public TreeNodeViewModel Parent { get; private set; }

        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private bool _isVisible = true;

        private bool? _isChecked = false;

        public bool? IsChecked
        {
            get => _isChecked;
            set => SetChecked(value, cascadeToChildren: true, notifyParent: true);
        }

        /// <summary>Fires whenever this node's or any descendant's IsChecked actually changes — the main ViewModel subscribes once per root to keep its status strip counts live.</summary>
        public event System.Action CheckedChanged;

        public TreeNodeViewModel(ElementTreeNodeKind kind, string name, int elementCount = 0, string owner = "", string editableBadge = "", IReadOnlyList<ElementId> elementIds = null, bool? isOpen = null)
        {
            Kind = kind;
            Name = name;
            ElementCount = elementCount;
            Owner = owner;
            EditableBadge = editableBadge;
            OpenBadge = isOpen == null ? string.Empty : (isOpen.Value ? "open" : "closed");
            IsClosed = isOpen == false;
            ElementIds = elementIds ?? System.Array.Empty<ElementId>();
        }

        public void AddChild(TreeNodeViewModel child)
        {
            child.Parent = this;
            child.CheckedChanged += () => CheckedChanged?.Invoke();
            Children.Add(child);
        }

        private void SetChecked(bool? value, bool cascadeToChildren, bool notifyParent)
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
            CheckedChanged?.Invoke();

            if (cascadeToChildren && value.HasValue)
            {
                foreach (var child in Children)
                    child.SetChecked(value, cascadeToChildren: true, notifyParent: false);
            }

            if (notifyParent)
                Parent?.RecomputeCheckedFromChildren();
        }

        private void RecomputeCheckedFromChildren()
        {
            if (Children.Count == 0) return;

            bool allChecked = Children.All(c => c.IsChecked == true);
            bool allUnchecked = Children.All(c => c.IsChecked == false);

            bool? newValue = allChecked ? true : allUnchecked ? false : (bool?)null;
            if (_isChecked == newValue) return;

            _isChecked = newValue;
            OnPropertyChanged(nameof(IsChecked));
            CheckedChanged?.Invoke();

            Parent?.RecomputeCheckedFromChildren();
        }

        /// <summary>True when this row has no child rows, i.e. it directly owns <see cref="ElementIds"/>.</summary>
        public bool IsLeaf => Children.Count == 0;

        /// <summary>Every leaf-row ElementId checked at or under this node.</summary>
        public IEnumerable<ElementId> CollectCheckedElementIds()
        {
            if (IsLeaf)
            {
                if (IsChecked == true)
                    foreach (var id in ElementIds)
                        yield return id;
                yield break;
            }
            foreach (var child in Children)
                foreach (var id in child.CollectCheckedElementIds())
                    yield return id;
        }

        /// <summary>Every ElementId at or under this node, whether checked or not.</summary>
        public IEnumerable<ElementId> CollectAllElementIds()
        {
            if (IsLeaf)
            {
                foreach (var id in ElementIds)
                    yield return id;
                yield break;
            }
            foreach (var child in Children)
                foreach (var id in child.CollectAllElementIds())
                    yield return id;
        }

        /// <summary>Walks this node and every descendant.</summary>
        public IEnumerable<TreeNodeViewModel> SelfAndDescendants()
        {
            yield return this;
            foreach (var child in Children)
                foreach (var d in child.SelfAndDescendants())
                    yield return d;
        }

        [RelayCommand]
        private void ToggleExpanded() => IsExpanded = !IsExpanded;

        /// <summary>
        /// Recursive name filter for the toolbar search box: a node stays
        /// visible if its own name matches or any descendant does, and any
        /// ancestor of a match auto-expands so the match is actually visible.
        /// Returns true if this node (or a descendant) matched.
        /// </summary>
        public bool ApplyFilter(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                IsVisible = true;
                foreach (var child in Children) child.ApplyFilter(query);
                return true;
            }

            bool selfMatch = Name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool anyChildMatch = false;
            foreach (var child in Children)
                anyChildMatch |= child.ApplyFilter(query);

            IsVisible = selfMatch || anyChildMatch;
            if (anyChildMatch) IsExpanded = true;
            return IsVisible;
        }
    }
}
