// =======================================================
// File: ProjectQueries.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// New in V032: read-only lookups the window needs (line styles, roof and
// view descriptions), moved out of the ViewModel so the ViewModel does not
// walk the Revit API itself. Call only in a Revit API context (command,
// external event, or a Revit event such as ViewActivated).
// =======================================================

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    public sealed class LineStyleInfo
    {
        public long IdValue { get; init; }
        public string Name { get; init; }
        public override string ToString() => Name;
    }

    public sealed class ViewInfo
    {
        public bool IsPlan { get; init; }
        public string Name { get; init; }
    }

    public static class ProjectQueries
    {
        /// <summary>Existing OST_Lines sub-categories — the list Revit shows as Line Styles.</summary>
        public static List<LineStyleInfo> GetLineStyles(Document doc)
        {
            var list = new List<LineStyleInfo>();
            Category lines = doc?.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines?.SubCategories == null) return list;

            foreach (Category sub in lines.SubCategories)
            {
                if (sub?.GetGraphicsStyle(GraphicsStyleType.Projection) is GraphicsStyle gs)
                    list.Add(new LineStyleInfo { IdValue = gs.Id.Value, Name = sub.Name });
            }
            return list.OrderBy(s => s.Name).ToList();
        }

        public static RoofInfo DescribeRoof(RoofBase roof)
        {
            if (roof == null) return new RoofInfo(0, "Unknown", "Unknown", 0);

            string level = "Unknown";
            if (roof.LevelId != null && roof.LevelId != ElementId.InvalidElementId)
                level = (roof.Document.GetElement(roof.LevelId) as Level)?.Name ?? "Unknown";

            double offsetMm = 0;
            Parameter offset = roof.get_Parameter(BuiltInParameter.ROOF_CONSTRAINT_OFFSET_PARAM);
            if (offset != null && offset.HasValue)
                offsetMm = RevitConvert.FtToMm(offset.AsDouble());

            return new RoofInfo(roof.Id.Value, roof.Name ?? "Unknown", level, offsetMm);
        }

        public static ViewInfo DescribeView(View view) => new ViewInfo
        {
            IsPlan = view is ViewPlan,
            Name = view?.Name ?? "(no view)"
        };
    }
}
