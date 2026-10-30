namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models
{
    /// <summary>How a bubble's detail number is written.</summary>
    public enum NumberStyle
    {
        /// <summary>1, 2, 3 … (the behaviour before 9.2.0).</summary>
        Numeric = 0,

        /// <summary>01, 02, 03 … padded to at least two digits.</summary>
        ZeroPadded = 1,

        /// <summary>A, B, C … Z, AA, AB …</summary>
        UpperLetters = 2,

        /// <summary>a, b, c … z, aa, ab …</summary>
        LowerLetters = 3
    }
}
