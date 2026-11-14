namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Where the filled region types for the hatching come from.</summary>
    public enum HatchTypeSource
    {
        /// <summary>The tool uses / creates its own types EPS2D_Fill_01 … (solid fill, palette colours) when missing.</summary>
        CreateToolTypes,

        /// <summary>The user picks existing filled region types for slots 1–4; more slots repeat them.</summary>
        PickExisting
    }
}
