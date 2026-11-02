namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models
{
    /// <summary>How detail numbers are written. <see cref="Numeric"/> is the pre-9.2 behaviour.</summary>
    public enum NumberingStyle
    {
        /// <summary>1, 2, 3 …</summary>
        Numeric = 0,
        /// <summary>01, 02, 03 … (padded to the widest number in the run, at least 2 digits)</summary>
        ZeroPadded = 1,
        /// <summary>a, b, c … z, aa, ab …</summary>
        LowerLetters = 2,
        /// <summary>A, B, C … Z, AA, AB …</summary>
        UpperLetters = 3
    }
}
