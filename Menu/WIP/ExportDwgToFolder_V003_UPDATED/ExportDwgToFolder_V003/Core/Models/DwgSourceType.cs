namespace Revit26_Plugin.ExportDwgToFolder.V003.Core.Models
{
    /// <summary>Whether a DWG came from a CAD link or a CAD import.</summary>
    public enum DwgSourceType
    {
        /// <summary>CAD link — the source file exists on disk; the DWG is copied directly.</summary>
        Link,

        /// <summary>CAD import — the DWG geometry is embedded in the Revit model.</summary>
        Import
    }
}
