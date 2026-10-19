using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Models
{
    /// <summary>
    /// Persisted via Shared/Services/SettingsService&lt;RoofNameBuilderSettings&gt; to
    /// %AppData%\Revit26_Plugin\RoofNameBuilder\settings.json. Load never throws — a missing or
    /// corrupt file falls back to these defaults (CLAUDE.md "Established architecture pattern").
    /// Every property has a default, so a V001 settings file loads cleanly into V002.
    /// </summary>
    public class RoofNameBuilderSettings
    {
        public List<NameCodeDefinition> CodeDefinitions { get; set; } = new()
        {
            new NameCodeDefinition { Code = "INS", Label = "Insulation", SourceParameter = "Insulation Thickness", Unit = "mm",  IncludeCodePrefix = true,  Order = 0 },
            new NameCodeDefinition { Code = "SLP", Label = "Slope",      SourceParameter = "Roof Slope",          Unit = "deg", IncludeCodePrefix = true,  Order = 1 },
            new NameCodeDefinition { Code = "MAT", Label = "Material",   SourceParameter = "Roof Material Type",  Unit = "",    IncludeCodePrefix = false, UppercaseValue = true, Order = 2 },
        };

        /// <summary>Separator inserted before each ticked code's segment, e.g. "_".</summary>
        public string TemplateSeparator { get; set; } = "_";

        /// <summary>
        /// Where "Instances" mode writes the built name. Roof instances have no settable Name —
        /// only Types do — so this targets an instance parameter instead (commonly "Mark").
        /// </summary>
        public string InstanceTargetParameter { get; set; } = "Mark";

        /// <summary>
        /// Default state of the "Hide compliant" filter. V002 meaning: hide rows that already carry
        /// at least one code and whose name is already up to date. (V001 removed every not-yet-pending
        /// row at load, which emptied the grid on first open.)
        /// </summary>
        public bool SkipAlreadyCompliant { get; set; } = true;

        /// <summary>Revit treats differently-cased names as colliding in some contexts — on by default to be safe.</summary>
        public bool CaseInsensitiveDuplicateCheck { get; set; } = true;

        /// <summary>Soft warning threshold — a built name longer than this flags IsTooLong on the row.</summary>
        public int NameLengthWarningThreshold { get; set; } = 100;

        /// <summary>
        /// V002: on load, recognise code segments already at the end of a name (e.g. "Roof_INS50_SLP2"),
        /// split them off into ticked codes and keep the rest as the base name — so re-running the
        /// tool updates codes instead of appending them a second time.
        /// </summary>
        public bool ParseExistingCodes { get; set; } = true;

        /// <summary>V002: last-used scope / mode, restored when the window opens.</summary>
        public RoofNameScope LastScope { get; set; } = RoofNameScope.AllInProject;
        public RoofNameMode LastMode { get; set; } = RoofNameMode.Types;
    }
}
