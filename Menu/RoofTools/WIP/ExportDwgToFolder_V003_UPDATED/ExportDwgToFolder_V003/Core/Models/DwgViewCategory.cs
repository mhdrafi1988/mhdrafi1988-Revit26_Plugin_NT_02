namespace Revit26_Plugin_Revised.ExportDwgToFolder.V003.Core.Models
{
    /// <summary>
    /// View category of the view that hosts a DWG instance.
    /// Priority order used for subfolder routing: Plan → Section → Drafting → NoViews.
    /// </summary>
    public enum DwgViewCategory
    {
        Plan,
        Section,
        Drafting,
        NoViews
    }
}
