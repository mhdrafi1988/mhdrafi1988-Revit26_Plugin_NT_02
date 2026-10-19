using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Models
{
    public enum RoofNameScope
    {
        AllInProject,
        ActiveView,
        UsedTypesOnly,
        CurrentSelection
    }

    public enum RoofNameMode
    {
        Types,
        Instances
    }

    /// <summary>
    /// One row's worth of source data read from Revit, plus the user's in-progress edits.
    /// No Revit API dependency — the element is carried as a raw long id so Core/ stays clean
    /// (ARCHITECTURE.md §3).
    /// </summary>
    public class RoofNameRow
    {
        public long ElementId { get; set; }

        /// <summary>Current Type.Name (Types mode) or current value of the target instance parameter (Instances mode).</summary>
        public string CurrentName { get; set; } = string.Empty;

        /// <summary>User-editable base the configured codes get appended to.</summary>
        public string BaseName { get; set; } = string.Empty;

        /// <summary>Number of placed instances of this type. Only meaningful in Types mode.</summary>
        public int InstanceCount { get; set; }

        /// <summary>Type name of the roof — shown for context in Instances mode.</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>code -> (ticked, value), e.g. "INS" -> (true, "50").</summary>
        public Dictionary<string, RoofCodeValue> Codes { get; set; } = new();
    }

    public class RoofCodeValue
    {
        public bool Ticked { get; set; }
        public string Value { get; set; } = string.Empty;
    }
}
