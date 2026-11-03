namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models
{
    /// <summary>
    /// How a curved edge is divided.
    ///   ByDistance → user spacing in metres; segment count rounded, spacing equalized.
    ///   ByCount    → a fixed number of points (place exactly N points).
    /// </summary>
    public enum DivisionMode
    {
        ByDistance,
        ByCount
    }
}
