using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.RoofTag.V018.Helpers;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Gets the candidate tag points: picked by hand, or the roof's shape-editor vertices.</summary>
    internal static class PointCollector
    {
        /// <summary>Points closer than this (10 mm) count as the same point.</summary>
        public const double DedupTolFt = 10.0 / 304.8;

        /// <summary>Collects the raw points for the chosen mode (not yet de-duplicated).</summary>
        public static List<XYZ> Collect(UIDocument uiDoc, RoofBase roof, bool manual)
        {
            if (manual)
            {
                IList<Reference> refs = uiDoc.Selection.PickObjects(ObjectType.PointOnElement, "Select points on roof");
                return refs.Select(r => r.GlobalPoint).ToList();
            }
            return RoofTagGeometryHelper.GetExactShapeVertices(roof);
        }

        /// <summary>Drops every point within <paramref name="tolFt"/> of an earlier one.</summary>
        public static List<XYZ> Deduplicate(IEnumerable<XYZ> points, double tolFt)
        {
            List<XYZ> unique = new();
            foreach (XYZ candidate in points)
            {
                if (!unique.Any(u => u.DistanceTo(candidate) <= tolFt))
                    unique.Add(candidate);
            }
            return unique;
        }
    }
}
