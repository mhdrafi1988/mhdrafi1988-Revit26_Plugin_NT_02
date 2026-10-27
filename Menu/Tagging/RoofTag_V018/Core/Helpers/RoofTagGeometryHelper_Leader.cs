using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.RoofTag.V018.Helpers
{
    internal static partial class RoofTagGeometryHelper
    {
        internal enum PlacementMode { Inward, Outward }

        public static (XYZ Bend, XYZ End) ComputeTwoStepLeaderPlacement(
            View          view,
            Element       element,
            XYZ           anchorPoint,
            double        bendOffsetFt,
            double        endOffsetFt,
            PlacementMode placementMode)
        {
            Transform viewInverse = view.CropBox.Transform.Inverse;

            BoundingBoxXYZ bbox = GetElementViewBoundingBox(element, view);
            if (bbox == null)
                return (anchorPoint, anchorPoint);

            XYZ center     = (bbox.Min + bbox.Max) * 0.5;
            XYZ anchorView = viewInverse.OfPoint(anchorPoint);

            XYZ right = view.RightDirection.Normalize();
            XYZ up    = view.UpDirection.Normalize();
            XYZ delta = anchorView - center;

            bool isTop   = delta.DotProduct(up)    >= 0;
            bool isRight = delta.DotProduct(right)  >= 0;

            XYZ diagView = CalculateDiagonalDirection(isTop, isRight, placementMode);
            XYZ diagModel = viewInverse.Inverse.OfVector(diagView).Normalize();
            XYZ bendPoint = anchorPoint + diagModel * bendOffsetFt;

            XYZ orthoView  = CalculateOrthogonalDirection(isTop, isRight, placementMode);
            XYZ orthoModel = viewInverse.Inverse.OfVector(orthoView).Normalize();
            XYZ endPoint   = bendPoint + orthoModel * endOffsetFt;

            return (bendPoint, endPoint);
        }

        private static XYZ CalculateDiagonalDirection(
            bool isTop, bool isRight, PlacementMode mode)
        {
            XYZ r = new XYZ(1, 0, 0);
            XYZ u = new XYZ(0, 1, 0);

            if (mode == PlacementMode.Outward)
            {
                if ( isTop &&  isRight) return  r +  u;
                if ( isTop && !isRight) return -r +  u;
                if (!isTop &&  isRight) return  r + -u;
                                        return -r + -u;
            }
            else
            {
                if ( isTop &&  isRight) return -r + -u;
                if ( isTop && !isRight) return  r + -u;
                if (!isTop &&  isRight) return -r +  u;
                                        return  r +  u;
            }
        }

        private static XYZ CalculateOrthogonalDirection(
            bool isTop, bool isRight, PlacementMode mode)
        {
            XYZ r = new XYZ(1, 0, 0);
            bool goRight = mode == PlacementMode.Outward ? isRight : !isRight;
            return goRight ? r : -r;
        }

        private static BoundingBoxXYZ GetElementViewBoundingBox(Element element, View view)
        {
            Options opts = new Options
            {
                View              = view,
                ComputeReferences = true
            };

            GeometryElement geo = element.get_Geometry(opts);
            if (geo == null) return null;

            BoundingBoxXYZ bbox = geo.GetBoundingBox();
            if (bbox == null) return null;

            Transform inv = view.CropBox.Transform.Inverse;
            return new BoundingBoxXYZ
            {
                Min = inv.OfPoint(bbox.Min),
                Max = inv.OfPoint(bbox.Max)
            };
        }
    }
}
