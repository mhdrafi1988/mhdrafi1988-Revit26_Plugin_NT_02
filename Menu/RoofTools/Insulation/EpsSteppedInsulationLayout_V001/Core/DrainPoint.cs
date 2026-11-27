namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core
{
    /// <summary>A drain location detected from shape editing vertices (plan position in metres).</summary>
    public readonly struct DrainPoint
    {
        public DrainPoint(Vec2 position, double elevationFeet)
        {
            Position = position;
            ElevationFeet = elevationFeet;
        }

        public Vec2 Position { get; }
        public double ElevationFeet { get; }
    }
}
