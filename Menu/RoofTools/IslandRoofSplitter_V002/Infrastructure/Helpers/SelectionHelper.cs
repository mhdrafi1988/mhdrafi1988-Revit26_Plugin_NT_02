// IslandRoofSplitter V002 — picking. Roof: exactly one FootPrintRoof. Lines: DetailCurves owned by the active view.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class SelectionHelper
    {
        public static FootPrintRoof PickRoof(UIDocument uidoc)
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new FootprintRoofFilter(),
                "Island Roof Splitter: pick ONE sloped footprint roof");
            var roof = (FootPrintRoof)uidoc.Document.GetElement(r);
            return roof;
        }

        public static List<DetailCurve> PickDetailCurves(UIDocument uidoc)
        {
            var view = uidoc.ActiveView;
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new DetailCurveFilter(view.Id),
                "Island Roof Splitter: pick the detail lines above the roof (this view), then Finish");
            var curves = refs.Select(x => uidoc.Document.GetElement(x)).OfType<DetailCurve>().ToList();
            if (curves.Count == 0) throw new InvalidOperationException("No detail lines were picked.");
            return curves;
        }

        private sealed class FootprintRoofFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FootPrintRoof;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class DetailCurveFilter : ISelectionFilter
        {
            private readonly ElementId _viewId;
            public DetailCurveFilter(ElementId viewId) { _viewId = viewId; }
            public bool AllowElement(Element elem) => elem is DetailCurve dc && dc.OwnerViewId == _viewId;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
