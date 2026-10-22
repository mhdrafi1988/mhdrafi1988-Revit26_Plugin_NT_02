using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models
{
    /// <summary>
    /// One detail line in the results grid. Built in the Revit API context by
    /// <see cref="Services.DetailLineScanner"/>; holds plain values only, so the UI never reads the model.
    /// </summary>
    public sealed partial class DetailLineRow : ObservableObject
    {
        /// <summary>Ticked in the grid's checkbox column.</summary>
        [ObservableProperty] private bool isChecked;

        /// <summary>True when the line is shorter than the current tolerance.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FlagsText))]
        private bool isShort;

        /// <summary>Element id of the detail line.</summary>
        public ElementId Id { get; init; }

        /// <summary>Element id as a number, for display, sorting and search.</summary>
        public long IdValue => Id?.Value ?? 0;

        /// <summary>Line style (subcategory of Lines) name.</summary>
        public string StyleName { get; init; } = string.Empty;

        /// <summary>Line style subcategory id, or null when the line has no readable style.</summary>
        public ElementId StyleCategoryId { get; init; }

        /// <summary>Projection line weight of the line style.</summary>
        public int StyleWeight { get; init; }

        /// <summary>Projection line weight override set on this element in its owner view, or 0 for none.</summary>
        public int OverrideWeight { get; init; }

        /// <summary>True when a per-view projection line weight override is set.</summary>
        public bool HasOverride => OverrideWeight > 0;

        /// <summary>Weight Revit draws the line with: the override when set, otherwise the style weight.</summary>
        public int EffectiveWeight => HasOverride ? OverrideWeight : StyleWeight;

        /// <summary>Owner view id.</summary>
        public ElementId ViewId { get; init; }

        /// <summary>Owner view name.</summary>
        public string ViewName { get; init; } = string.Empty;

        /// <summary>Owner view type (Floor Plan, Section, Drafting View, …).</summary>
        public string ViewTypeName { get; init; } = string.Empty;

        /// <summary>Curve length in millimetres.</summary>
        public double LengthMm { get; init; }

        /// <summary>Geometry kind.</summary>
        public CurveKind Curve { get; init; }

        /// <summary>Workset name, or empty in a non-workshared model.</summary>
        public string WorksetName { get; init; } = string.Empty;

        /// <summary>Instance comments, or empty.</summary>
        public string Comment { get; init; } = string.Empty;

        /// <summary>True when the line is a member of a group.</summary>
        public bool InGroup { get; init; }

        /// <summary>Name of the other user who owns the line or its workset, or null when it is editable.</summary>
        public string OwnedBy { get; init; }

        /// <summary>Duplicate cluster number, or 0 when the line has no duplicate.</summary>
        public int DuplicateCluster { get; internal set; }

        /// <summary>True when the line has at least one duplicate in its view.</summary>
        public bool IsDuplicate => DuplicateCluster > 0;

        /// <summary>True when the line is owned by another user.</summary>
        public bool IsOwnedByOther => !string.IsNullOrEmpty(OwnedBy);

        /// <summary>Flags column text: Duplicate, Short, In group, Owned by &lt;user&gt;.</summary>
        public string FlagsText
        {
            get
            {
                var flags = new List<string>(4);
                if (IsDuplicate) flags.Add("Duplicate");
                if (IsShort) flags.Add("Short");
                if (InGroup) flags.Add("In group");
                if (IsOwnedByOther) flags.Add("Owned by " + OwnedBy);
                return string.Join(" · ", flags);
            }
        }

        /// <summary>Recomputes <see cref="IsShort"/> for <paramref name="toleranceMm"/>.</summary>
        public void ApplyTolerance(double toleranceMm) => IsShort = LengthMm < toleranceMm;
    }
}
