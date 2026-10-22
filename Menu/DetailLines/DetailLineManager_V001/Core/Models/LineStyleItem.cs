using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models
{
    /// <summary>
    /// One line style (subcategory of Lines) with its project-wide usage. Used by the search
    /// checklist, the unused-style checklist and the Assign line style picker.
    /// </summary>
    public sealed partial class LineStyleItem : ObservableObject
    {
        /// <summary>Ticked in the checklist it is shown in.</summary>
        [ObservableProperty] private bool isChecked = true;

        /// <summary>
        /// True when the style matches a "Keep styles matching" pattern; it is then never deleted.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSelect), nameof(DisplayName))]
        private bool isKeptByPattern;

        /// <summary>Line style subcategory id.</summary>
        public ElementId CategoryId { get; init; }

        /// <summary>Projection graphics style id (the value <c>CurveElement.LineStyle</c> takes).</summary>
        public ElementId GraphicsStyleId { get; init; }

        /// <summary>Line style name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Projection line weight.</summary>
        public int Weight { get; init; }

        /// <summary>
        /// Number of curve elements in the whole project (model, detail and sketch lines) that use it.
        /// Meaningful only when <see cref="IsUsageKnown"/> is true.
        /// </summary>
        public int UseCount { get; init; }

        /// <summary>False when usage was not counted (the Delete unused tab was not open during the search).</summary>
        public bool IsUsageKnown { get; init; } = true;

        /// <summary>True for Revit's own styles (Thin Lines, &lt;Hidden&gt;, …), which are never deleted.</summary>
        public bool IsBuiltIn { get; init; }

        /// <summary>True when the style is known to be unused and is not built-in.</summary>
        public bool IsUnused => IsUsageKnown && !IsBuiltIn && UseCount == 0;

        /// <summary>True when the style may be ticked for deletion.</summary>
        public bool CanSelect => !IsKeptByPattern;

        /// <summary>Name shown in the unused-style checklist.</summary>
        public string DisplayName => IsKeptByPattern ? Name + "  (kept: matches a keep pattern)" : Name;

        /// <summary>Use count for the search checklist; empty when not counted.</summary>
        public string CountText => IsUsageKnown ? UseCount.ToString(CultureInfo.CurrentCulture) : string.Empty;

        /// <summary>Copy with its own check state, for the unused-style checklist.</summary>
        public LineStyleItem Clone() => new()
        {
            CategoryId = CategoryId,
            GraphicsStyleId = GraphicsStyleId,
            Name = Name,
            Weight = Weight,
            UseCount = UseCount,
            IsUsageKnown = IsUsageKnown,
            IsBuiltIn = IsBuiltIn
        };

        /// <inheritdoc/>
        public override string ToString() => Name;
    }
}
