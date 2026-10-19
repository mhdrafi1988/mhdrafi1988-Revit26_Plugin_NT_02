namespace Revit26_Plugin.RoofNameBuilder.V002.Core.Models
{
    /// <summary>
    /// One configurable naming code (e.g. INS, SLP, MAT) and how its value is sourced.
    /// Pure POCO — zero Revit API dependency (Core/ convention, see CLAUDE.md).
    /// Persisted as part of <see cref="RoofNameBuilderSettings"/> via Shared/Services/SettingsService&lt;T&gt;.
    /// </summary>
    public class NameCodeDefinition
    {
        /// <summary>Short code shown in the grid header and used in the built name, e.g. "INS". Letters/digits only.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Human label shown in the Code Definitions panel, e.g. "Insulation".</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Revit parameter name read from the roof (instance first, then its type) to seed this code's value.</summary>
        public string SourceParameter { get; set; } = string.Empty;

        /// <summary>Display-only unit suffix shown in the grid header, e.g. "mm", "deg". Not written into the name.</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// True: the built segment is "{Code}{Value}" (e.g. "_INS50").
        /// False: the built segment is just the value (e.g. "_SLATE") — used for MAT by default.
        /// </summary>
        public bool IncludeCodePrefix { get; set; } = true;

        /// <summary>True: the value is upper-cased when appended to the name (e.g. "slate" -> "SLATE").</summary>
        public bool UppercaseValue { get; set; }

        /// <summary>Position in the name template — codes are appended in ascending Order.</summary>
        public int Order { get; set; }
    }
}
