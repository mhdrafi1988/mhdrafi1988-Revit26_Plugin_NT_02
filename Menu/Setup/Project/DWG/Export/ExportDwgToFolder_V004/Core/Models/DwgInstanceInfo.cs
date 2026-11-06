using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models
{
    /// <summary>
    /// Describes one ImportInstance that is a CAD link or import found in the document.
    /// </summary>
    public class DwgInstanceInfo
    {
        /// <summary>Revit element ID of the ImportInstance.</summary>
        public ElementId ElementId { get; set; }

        /// <summary>Display name of the import (usually the original file's base name).</summary>
        public string SymbolName { get; set; }

        /// <summary>Whether this came from a CAD link or a CAD import.</summary>
        public DwgSourceType SourceType { get; set; }

        /// <summary>View type that hosts this DWG, used for subfolder routing.</summary>
        public DwgViewCategory ViewCategory { get; set; }

        /// <summary>Name of the host view, or null when the DWG is in model space with no clear view.</summary>
        public string ViewName { get; set; }

        /// <summary>ID of the host view, or ElementId.InvalidElementId for model-space elements.</summary>
        public ElementId OwnerViewId { get; set; }

        /// <summary>Sheet number + name if the host view is placed on a sheet, otherwise "Not on Sheet".</summary>
        public string SheetInfo { get; set; }

        /// <summary>Absolute path of the source DWG file for linked imports; null for embedded imports.</summary>
        public string LinkedSourcePath { get; set; }

        /// <summary>
        /// How many layers this CAD instance brings into the model, read from its layer
        /// subcategories at scan time. NEW in V004 — a row showing 1 layer tells you the layer
        /// structure was already flattened when the DWG came in, which is the real cause when an
        /// export comes out on a single layer. -1 means it could not be read.
        /// </summary>
        public int LayerCount { get; set; } = -1;

        /// <summary>Proposed output file name (without folder, with .dwg extension).</summary>
        public string OutputFileName { get; set; }
    }
}
