using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorNameBuilder.V001.Core.Models
{
    public enum FloorNameScope
    {
        AllInProject,
        ActiveView,
        UsedTypesOnly,
        CurrentSelection
    }

    public enum FloorNameMode
    {
        Types,
        Instances
    }

    /// <summary>
    /// One row's worth of source data read from Revit, plus the user's in-progress edits.
    /// No Revit API dependency — the element is carried as a raw long id so Core/ stays clean
    /// (ARCHITECTURE.md §3).
    /// </summary>
    public class FloorNameRow
    {
        public long ElementId { get; set; }

        /// <summary>Current Type.Name (Types mode) or current value of the target instance parameter (Instances mode).</summary>
        public string CurrentName { get; set; } = string.Empty;

        /// <summary>User-editable base the configured codes get appended to.</summary>
        public string BaseName { get; set; } = string.Empty;

        /// <summary>Number of placed instances of this type. Only meaningful in Types mode.</summary>
        public int InstanceCount { get; set; }

        /// <summary>Type name of the floor — shown for context in Instances mode.</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>code -> (ticked, value), e.g. "INS" -> (true, "50").</summary>
        public Dictionary<string, FloorCodeValue> Codes { get; set; } = new();
    }

    public class FloorCodeValue
    {
        public bool Ticked { get; set; }
        public string Value { get; set; } = string.Empty;
    }
}
