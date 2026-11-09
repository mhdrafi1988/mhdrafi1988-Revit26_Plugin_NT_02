using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Services
{
    /// <summary>Draws the validated CurveLoop as new Detail Lines/Arcs in the active view. Must run inside an open Transaction.</summary>
    public static class DetailLineDrawService
    {
        public static List<ElementId> Draw(Document doc, View view, CurveLoop loop)
        {
            var ids = new List<ElementId>();
            foreach (Curve c in loop)
            {
                ToolCancellation.CheckCurrent();
                DetailCurve dc = doc.Create.NewDetailCurve(view, c);
                ids.Add(dc.Id);
            }
            return ids;
        }
    }
}
