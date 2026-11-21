using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels
{
    /// <summary>
    /// Shared flag that stops a tick cascading back on itself: set while a parent is writing its
    /// children's ticks, or a child is writing its parent's, so each change settles once.
    /// </summary>
    public sealed class CascadeGate
    {
        /// <summary>True while a cascade is in progress; node handlers stand down.</summary>
        public bool Suppressed { get; set; }
    }

    /// <summary>
    /// One element type visible in the run's sections — the leaf the user ticks. Roofs and walls
    /// can be ticked; every other category is listed for reading and export only, because the
    /// tool draws nothing from it.
    /// </summary>
    public partial class TypeNode : ObservableObject
    {
        private readonly CascadeGate _gate;

        /// <summary>Creates the node under <paramref name="parent"/>.</summary>
        public TypeNode(CascadeGate gate, CategoryNode parent)
        {
            _gate = gate;
            Parent = parent;
        }

        /// <summary>The category node this type sits under.</summary>
        public CategoryNode Parent { get; }

        /// <summary>Which type, in which document.</summary>
        public TypeKey Key { get; set; }

        /// <summary>"Family : Type", as the Project Browser would read it.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>What the tool can do with this type.</summary>
        public ElementRole Role { get; set; }

        /// <summary>How many of its instances the run's sections show.</summary>
        public int InstanceCount { get; set; }

        /// <summary>How many of the run's sections show at least one of them.</summary>
        public int SectionCount { get; set; }

        /// <summary>Instance and section counts, for the row's second line.</summary>
        public string Meta => SectionCount == 1
            ? $"{InstanceCount} in 1 section"
            : $"{InstanceCount} in {SectionCount} sections";

        /// <summary>True when this type can be ticked — roofs and walls only.</summary>
        public bool IsSelectable => Role != ElementRole.Listed;

        /// <summary>Hover text: what a tick does, or why there is none.</summary>
        public string Hint => Role switch
        {
            ElementRole.Roof => "Ticked: the whole cut profile of every one of these roofs is traced in each section that cuts it.",
            ElementRole.Wall => "Ticked: a vertical line is drawn on these walls where they stand next to a ticked roof's cut profile.",
            _ => "Listed so you can see what the section shows. The tool draws lines from roofs and walls only."
        };

        [ObservableProperty]
        private bool _isChecked;

        /// <summary>True while this row passes the search text and the category filter.</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        partial void OnIsCheckedChanged(bool value)
        {
            if (_gate.Suppressed) return;
            Parent?.RefreshFromChildren();
        }
    }

    /// <summary>One category of one source, holding its types.</summary>
    public partial class CategoryNode : ObservableObject
    {
        private readonly CascadeGate _gate;

        /// <summary>Creates the node under <paramref name="parent"/>.</summary>
        public CategoryNode(CascadeGate gate, SourceNode parent)
        {
            _gate = gate;
            Parent = parent;
        }

        /// <summary>The source node this category sits under.</summary>
        public SourceNode Parent { get; }

        /// <summary>The category's name.</summary>
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>What the tool can do with this category.</summary>
        public ElementRole Role { get; set; }

        /// <summary>Its types, in name order.</summary>
        public ObservableCollection<TypeNode> Types { get; } = new();

        /// <summary>Instances of all its types that the run's sections show.</summary>
        public int InstanceCount => Types.Sum(t => t.InstanceCount);

        /// <summary>Instance count and what a tick does, for the row.</summary>
        public string Meta => Role == ElementRole.Listed
            ? $"{InstanceCount} shown — listed only"
            : $"{InstanceCount} shown";

        /// <summary>True when any of its types can be ticked.</summary>
        public bool IsSelectable => Role != ElementRole.Listed;

        [ObservableProperty]
        private bool _isExpanded;

        /// <summary>Tri-state over its types: null when some are ticked and some are not.</summary>
        [ObservableProperty]
        private bool? _isChecked = false;

        /// <summary>True while any of its types passes the current filters.</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        partial void OnIsCheckedChanged(bool? value)
        {
            if (_gate.Suppressed || !value.HasValue) return;

            _gate.Suppressed = true;
            try
            {
                foreach (TypeNode type in Types)
                    if (type.IsSelectable && type.IsVisible) type.IsChecked = value.Value;
            }
            finally
            {
                _gate.Suppressed = false;
            }

            Parent?.RefreshFromChildren();
        }

        /// <summary>Reads its tick back from its types, then lets its source do the same.</summary>
        public void RefreshFromChildren()
        {
            List<TypeNode> selectable = Types.Where(t => t.IsSelectable).ToList();
            bool? state = selectable.Count == 0
                ? false
                : selectable.All(t => t.IsChecked) ? true
                : selectable.All(t => !t.IsChecked) ? (bool?)false
                : null;

            SetChecked(state);
            Parent?.RefreshFromChildren();
        }

        /// <summary>Sets the tick without cascading — used by the refreshes and by Tick all.</summary>
        public void SetChecked(bool? state)
        {
            bool previous = _gate.Suppressed;
            _gate.Suppressed = true;
            try { IsChecked = state; }
            finally { _gate.Suppressed = previous; }
        }

        /// <summary>Tells the view to read <see cref="InstanceCount"/> and <see cref="Meta"/> again.</summary>
        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(InstanceCount));
            OnPropertyChanged(nameof(Meta));
        }
    }

    /// <summary>The host model, or one loaded link, holding its categories.</summary>
    public partial class SourceNode : ObservableObject
    {
        private readonly CascadeGate _gate;

        /// <summary>Creates the node.</summary>
        public SourceNode(CascadeGate gate) => _gate = gate;

        /// <summary>Id of the link instance, or 0 for the host model.</summary>
        public long LinkInstanceId { get; set; }

        /// <summary>"Host model", or the link instance's name.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Where these elements come from.</summary>
        public ElementSourceKind Source { get; set; }

        /// <summary>Its categories, roofs and walls first.</summary>
        public ObservableCollection<CategoryNode> Categories { get; } = new();

        /// <summary>Elements of this source that the run's sections show.</summary>
        public int InstanceCount => Categories.Sum(c => c.InstanceCount);

        /// <summary>Element count, for the row.</summary>
        public string Meta => $"{InstanceCount} element(s) in the sections read";

        [ObservableProperty]
        private bool _isExpanded = true;

        /// <summary>Tri-state over its categories.</summary>
        [ObservableProperty]
        private bool? _isChecked = false;

        /// <summary>True while any of its categories passes the current filters.</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        partial void OnIsCheckedChanged(bool? value)
        {
            if (_gate.Suppressed || !value.HasValue) return;

            _gate.Suppressed = true;
            try
            {
                foreach (CategoryNode category in Categories)
                {
                    if (!category.IsSelectable || !category.IsVisible) continue;
                    category.SetChecked(value.Value);
                    foreach (TypeNode type in category.Types)
                        if (type.IsSelectable && type.IsVisible) type.IsChecked = value.Value;
                }
            }
            finally
            {
                _gate.Suppressed = false;
            }
        }

        /// <summary>Reads its tick back from its categories.</summary>
        public void RefreshFromChildren()
        {
            List<CategoryNode> selectable = Categories.Where(c => c.IsSelectable).ToList();
            bool? state = selectable.Count == 0
                ? false
                : selectable.All(c => c.IsChecked == true) ? true
                : selectable.All(c => c.IsChecked == false) ? (bool?)false
                : null;

            bool previous = _gate.Suppressed;
            _gate.Suppressed = true;
            try { IsChecked = state; }
            finally { _gate.Suppressed = previous; }
        }

        /// <summary>Tells the view to read <see cref="InstanceCount"/> and <see cref="Meta"/> again.</summary>
        public void RefreshCounts()
        {
            foreach (CategoryNode category in Categories) category.RefreshCounts();
            OnPropertyChanged(nameof(InstanceCount));
            OnPropertyChanged(nameof(Meta));
        }
    }
}
