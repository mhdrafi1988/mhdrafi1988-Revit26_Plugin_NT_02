namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>How a row starting from its middle (<see cref="RowStartMode.FromMiddle"/>) is set out.</summary>
    public enum MiddleStartVariant
    {
        /// <summary>Per face, whichever of D1 / D2 leaves fewer pieces under the minimum size, then more full boards.</summary>
        Auto,

        /// <summary>D1 — a joint on the centre line, boards outward both ways.</summary>
        JointOnCentre,

        /// <summary>D2 — a full board centred on the centre line, boards outward both ways.</summary>
        BoardCentred
    }
}
