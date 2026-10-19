using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Models
{
    /// <summary>One element's planned rename, handed to the ExternalEvent handler.</summary>
    public class RenameItem
    {
        public long ElementId { get; set; }
        public string NewName { get; set; } = string.Empty;

        /// <summary>True: write via Element.Name (RoofType — a global rename). False: write to InstanceParameterName.</summary>
        public bool IsType { get; set; }

        /// <summary>Instance parameter the built name is written to in Instances mode (default "Mark" — see RoofNameBuilderSettings.InstanceTargetParameter).</summary>
        public string InstanceParameterName { get; set; } = string.Empty;
    }

    public class RenamePayload
    {
        public List<RenameItem> Items { get; set; } = new();
    }

    /// <summary>Per-element outcome reported back from the handler after the batch TransactionGroup.</summary>
    public class RenameRowResult
    {
        public long ElementId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>V002: Revit warnings raised (and dismissed) while committing this row, e.g. duplicate Mark values.</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
