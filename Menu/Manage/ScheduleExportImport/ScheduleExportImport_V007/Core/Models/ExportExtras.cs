using System.Collections.Generic;

namespace Revit26_Plugin_Revised.ScheduleExportImport.V007.Core.Models
{
    /// <summary>Extras for the Excel export beyond headers and rows.</summary>
    public class ExportExtras
    {
        /// <summary>Allowed values per header (element names for Base Level etc., Yes/No) → Excel dropdown lists.</summary>
        public Dictionary<string, List<string>> Dropdowns { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>Headers of type-parameter columns (importable only when ticked in the preview).</summary>
        public HashSet<string> TypeHeaders { get; set; } = new HashSet<string>();
    }

    /// <summary>Persisted per user in %AppData%\Revit26_Plugin_Revised\ScheduleExportImport\settings.json.</summary>
    public class ScheduleExportImportSettings
    {
        public string LastFolder { get; set; } = string.Empty;
        public bool IncludeHiddenFields { get; set; }
    }
}
