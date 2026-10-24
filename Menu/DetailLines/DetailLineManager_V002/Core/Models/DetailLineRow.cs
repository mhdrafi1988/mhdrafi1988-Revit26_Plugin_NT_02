using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Globalization;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models
{
    /// <summary>
    /// One detail line in the results grid. Built in the Revit API context by
    /// <see cref="Services.ScanJob"/>; holds plain values only, so the UI never reads the model.
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

        /// <summary>
        /// Projection line weight the owner view's Visibility/Graphics (or the view template that
        /// controls it) sets for this line style or for Lines, or 0 for none. View filters are not read.
        /// </summary>
        public int VgWeight { get; init; }

        /// <summary>True when a per-element projection line weight override is set.</summary>
        public bool HasOverride => OverrideWeight > 0;

        /// <summary>True when the view's Visibility/Graphics overrides the weight of this line style.</summary>
        public bool HasVgOverride => VgWeight > 0;

        /// <summary>
        /// Weight Revit draws the line with: the element override when set, else the view's
        /// Visibility/Graphics weight, else the style weight.
        /// </summary>
        public int EffectiveWeight => HasOverride ? OverrideWeight : HasVgOverride ? VgWeight : StyleWeight;

        /// <summary>Badge after the weight: "ovr" (element override), "vg" (view Visibility/Graphics) or empty.</summary>
        public string WeightMarker => HasOverride ? "ovr" : HasVgOverride ? "vg" : string.Empty;

        /// <summary>True when <see cref="WeightMarker"/> is not empty.</summary>
        public bool HasWeightMarker => HasOverride || HasVgOverride;

        /// <summary>Tooltip explaining where the weight comes from.</summary>
        public string WeightTip
        {
            get
            {
                string tip = $"Style weight {StyleWeight}";
                if (HasVgOverride) tip += $" · view V/G {VgWeight}";
                if (HasOverride) tip += $" · element override {OverrideWeight}";
                return tip + ". View filters are not included.";
            }
        }

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

        /// <summary>Id of the group the line belongs to, or null.</summary>
        public ElementId GroupId { get; init; }

        /// <summary>Name of the group the line belongs to, or empty.</summary>
        public string GroupName { get; init; } = string.Empty;

        /// <summary>True when the line is a member of a group.</summary>
        public bool InGroup => GroupId != null;

        /// <summary>Name of the other user who owns the line or its workset, or null when it is editable.</summary>
        public string OwnedBy { get; init; }

        /// <summary>Duplicate cluster number, or 0 when the line has no duplicate.</summary>
        public int DuplicateCluster { get; internal set; }

        /// <summary>True when the line has at least one duplicate in its view.</summary>
        public bool IsDuplicate => DuplicateCluster > 0;

        /// <summary>
        /// Overlap group number: straight lines of the same style in the same view that lie on top of
        /// each other without being exact duplicates. 0 when the line overlaps nothing.
        /// </summary>
        public int OverlapGroup { get; internal set; }

        /// <summary>True when the line overlaps another line of the same style.</summary>
        public bool IsOverlapping => OverlapGroup > 0;

        /// <summary>Id of a longer line of the same style that fully covers this one, or 0.</summary>
        public long CoveredBy { get; internal set; }

        /// <summary>True when a longer line of the same style fully covers this one.</summary>
        public bool IsCovered => CoveredBy > 0;

        /// <summary>True when the line is owned by another user.</summary>
        public bool IsOwnedByOther => !string.IsNullOrEmpty(OwnedBy);

        /// <summary>Flags column text: Duplicate, Covered / Overlaps, Short, In group, Owned by &lt;user&gt;.</summary>
        public string FlagsText
        {
            get
            {
                var flags = new List<string>(5);
                if (IsDuplicate) flags.Add("Duplicate");
                if (IsCovered) flags.Add("Covered by " + CoveredBy.ToString(CultureInfo.InvariantCulture));
                else if (IsOverlapping) flags.Add("Overlaps");
                if (IsShort) flags.Add("Short");
                if (InGroup) flags.Add(string.IsNullOrEmpty(GroupName) ? "In group" : $"In group '{GroupName}'");
                if (IsOwnedByOther) flags.Add("Owned by " + OwnedBy);
                return string.Join(" · ", flags);
            }
        }

        /// <summary>Recomputes <see cref="IsShort"/> for <paramref name="toleranceMm"/>.</summary>
        public void ApplyTolerance(double toleranceMm) => IsShort = LengthMm < toleranceMm;
    }
}
