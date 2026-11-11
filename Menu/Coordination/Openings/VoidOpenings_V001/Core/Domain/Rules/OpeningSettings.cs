namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules
{
    /// <summary>Which way an opening points.</summary>
    public enum OpeningAxisMode
    {
        /// <summary>
        /// Square to the structure: along the face normal of a wall or floor, and perpendicular to
        /// the length of a beam or column. This is the default.
        /// </summary>
        PerpendicularToStructure,
        /// <summary>Along the MEP element, whatever its slope.</summary>
        FollowMep,
    }

    /// <summary>Run-wide options for sizing openings. Lengths are millimetres.</summary>
    public sealed class OpeningSettings
    {
        /// <summary>How far the void extends beyond the structure on each side, so it always cuts clean through.</summary>
        public double OvershootMm { get; set; } = 50;

        /// <summary>Opening sizes are rounded up to a multiple of this; 0 turns rounding off.</summary>
        public double RoundUpToMm { get; set; } = 0;

        /// <summary>True to add insulation thickness to the MEP size.</summary>
        public bool IncludeInsulation { get; set; } = true;

        /// <summary>Which way openings point.</summary>
        public OpeningAxisMode AxisMode { get; set; } = OpeningAxisMode.PerpendicularToStructure;

        /// <summary>
        /// A round element crossing at up to this angle from the opening axis keeps a circular
        /// opening, enlarged to cover the slope; beyond it the opening becomes rectangular (unless
        /// the rule forces a shape).
        /// </summary>
        public double MaxCircularAngleDeg { get; set; } = 15;
    }
}
