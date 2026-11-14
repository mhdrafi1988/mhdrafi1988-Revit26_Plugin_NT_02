using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Face i = outline ∩ { Lj − Li ≥ 0 for all j ≠ i }.</summary>
    public static class SlopeFaceBuilder
    {
        public static IReadOnlyList<SlopeFace> Build(EdgeDistanceField field)
        {
            var faces = new List<SlopeFace>(field.EdgeCount);
            for (int i = 0; i < field.EdgeCount; i++)
            {
                IEnumerable<HalfPlane> planes = Enumerable.Range(0, field.EdgeCount)
                    .Where(j => j != i)
                    .Select(j => field.CloserToEdgeHalfPlane(i, j));

                Polygon2D face = ConvexClipper.ClipAll(field.Outline, planes);
                if (!face.IsEmpty && face.Area > GeometryTolerance.Epsilon)
                {
                    faces.Add(new SlopeFace(i, face.ToCounterClockwise()));
                }
            }

            return faces;
        }
    }
}
