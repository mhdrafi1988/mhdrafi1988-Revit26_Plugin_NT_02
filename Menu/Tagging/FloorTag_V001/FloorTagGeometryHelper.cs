using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTag.V001.Helpers
{
    internal static partial class FloorTagGeometryHelper
    {
        /// <summary>
        /// Projects <paramref name="inputPoint"/> onto the nearest upward-facing
        /// face of the floor solid and returns its Reference.
        /// This is equivalent to a manual mouse click on the floor surface.
        /// Returns false if no suitable face is found.
        /// </summary>
        public static bool GetTaggingReferenceOnFloor(
            Element   floor,
            XYZ       inputPoint,
            out Reference faceReference,
            out XYZ       projectedPoint)
        {
            faceReference  = null;
            projectedPoint = null;

            if (floor == null || inputPoint == null)
                return false;

            Options options = new Options
            {
                ComputeReferences        = true,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geomElem = floor.get_Geometry(options);
            if (geomElem == null) return false;

            double minDistance = double.MaxValue;

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is not Solid solid || solid.Faces.IsEmpty)
                    continue;

                foreach (Face face in solid.Faces)
                {
                    IntersectionResult result = face.Project(inputPoint);
                    if (result == null) continue;

                    XYZ p  = result.XYZPoint;
                    UV  uv = result.UVPoint;
                    if (p == null || uv == null) continue;

                    XYZ normal;
                    try   { normal = face.ComputeNormal(uv); }
                    catch { continue; }

                    if (normal.Z < 0.2) continue;

                    double dist = inputPoint.DistanceTo(p);
                    if (dist >= minDistance) continue;

                    minDistance    = dist;
                    faceReference  = face.Reference;
                    projectedPoint = p;
                }
            }

            return faceReference != null;
        }
    }
}
