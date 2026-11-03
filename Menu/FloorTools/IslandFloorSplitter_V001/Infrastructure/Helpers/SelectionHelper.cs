// Island Floor Splitter V001 (floor port of Island Roof Splitter V002) — picking. Source: exactly one Floor
// (only read, never changed). Lines: DetailCurves owned by the active view.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.IslandFloorSplitter.V001.Infrastructure.Helpers
{
    public static class SelectionHelper
    {
        /// <summary>Picks the source floor. The name is kept from the roof tool so the callers stay unchanged.</summary>
        public static Floor PickRoof(UIDocument uidoc)
        {
            var r = uidoc.Selection.PickObject(ObjectType.Element, new FloorFilter(),
                "Island Floor Splitter: pick ONE floor");
            return (Floor)uidoc.Document.GetElement(r);
        }

        public static List<DetailCurve> PickDetailCurves(UIDocument uidoc)
        {
            var view = uidoc.ActiveView;
            var refs = uidoc.Selection.PickObjects(ObjectType.Element, new DetailCurveFilter(view.Id),
                "Island Floor Splitter: pick the detail lines over the floor (this view), then Finish");
            var curves = refs.Select(x => uidoc.Document.GetElement(x)).OfType<DetailCurve>().ToList();
            if (curves.Count == 0) throw new InvalidOperationException("No detail lines were picked.");
            return curves;
        }

        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Floor;
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
