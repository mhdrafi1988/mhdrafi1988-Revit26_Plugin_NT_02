using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

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

        /// <summary>Line style subcategory id.</summary>
        public ElementId CategoryId { get; init; }

        /// <summary>Projection graphics style id (the value <c>CurveElement.LineStyle</c> takes).</summary>
        public ElementId GraphicsStyleId { get; init; }

        /// <summary>Line style name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Projection line weight.</summary>
        public int Weight { get; init; }

        /// <summary>Number of curve elements in the whole project (model, detail and sketch lines) that use it.</summary>
        public int UseCount { get; init; }

        /// <summary>True for Revit's own styles (Thin Lines, &lt;Hidden&gt;, …), which are never deleted.</summary>
        public bool IsBuiltIn { get; init; }

        /// <summary>True when the style can be deleted as unused.</summary>
        public bool IsUnused => !IsBuiltIn && UseCount == 0;

        /// <summary>Use count for the checklist, e.g. "12" or "0 uses".</summary>
        public string UseText => UseCount == 1 ? "1 use" : $"{UseCount} uses";

        /// <inheritdoc/>
        public override string ToString() => Name;
    }
}
