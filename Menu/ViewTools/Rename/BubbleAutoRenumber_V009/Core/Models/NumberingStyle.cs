namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models
{
    /// <summary>
    /// What the detail-number sequence is written with (NEW in 9.3.0).
    /// </summary>
    public enum NumberingStyle
    {
        /// <summary>1, 2, 3 … — the only style before 9.3.0, and still the default.</summary>
        Numbers = 0,

        /// <summary>A, B, C … Z, AA, AB … (capital letters, spreadsheet-column order).</summary>
        Letters = 1
    }
}
