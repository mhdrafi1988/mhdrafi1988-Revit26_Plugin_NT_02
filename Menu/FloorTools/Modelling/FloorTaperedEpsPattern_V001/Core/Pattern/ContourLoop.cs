namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>One closed thickness contour (convex, CCW) at <see cref="ThicknessMm"/>.</summary>
    public sealed class ContourLoop
    {
        public ContourLoop(double thicknessMm, Polygon2D polygon)
        {
            ThicknessMm = thicknessMm;
            Polygon = polygon;
        }

        public double ThicknessMm { get; }
        public Polygon2D Polygon { get; }
    }
}
