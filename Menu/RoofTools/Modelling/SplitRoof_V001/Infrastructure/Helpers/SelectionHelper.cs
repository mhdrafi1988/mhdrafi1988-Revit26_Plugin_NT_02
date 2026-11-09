// Split Roof V001 — gets the footprint roof and the straight cut lines (pre-selection or picks) and validates them.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Infrastructure.Helpers
{
    /// <summary>Selection of the roof to split and the lines to split it with.</summary>
    public static class SelectionHelper
    {
        /// <summary>
        /// Returns the footprint roof (the one pre-selected, otherwise picked). Throws <see cref="InvalidOperationException"/>
        /// for a non-footprint roof, a read-only document or a roof checked out by another user. A cancelled pick propagates
        /// as <see cref="Autodesk.Revit.Exceptions.OperationCanceledException"/>.
        /// </summary>
        public static FootPrintRoof GetRoof(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var roofs = uidoc.Selection.GetElementIds().Select(doc.GetElement).Where(e => e is RoofBase).ToList();
            Element chosen = roofs.Count == 1
                ? roofs[0]
                : doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new Filter(e => e is RoofBase), "Select the footprint roof to split."));
            if (chosen is not FootPrintRoof roof)
                throw new InvalidOperationException($"Roof {chosen.Id} is not a footprint roof (extrusion roofs are not supported).");
            if (doc.IsReadOnly) throw new InvalidOperationException("The document is read-only.");
            if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, roof.Id) == CheckoutStatus.OwnedByOtherUser)
                throw new InvalidOperationException($"Roof {roof.Id} is checked out by another user.");
            return roof;
        }

        /// <summary>
        /// Returns the cut lines (pre-selected detail/model lines, otherwise picked). Curved elements are rejected with a clear message;
        /// vertical lines are rejected because they have no direction in plan.
        /// </summary>
        public static IReadOnlyList<CutLine> GetCutLines(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var chosen = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<CurveElement>().ToList();
            if (chosen.Count == 0)
                chosen = uidoc.Selection.PickObjects(ObjectType.Element, new Filter(e => e is CurveElement),
                    "Select the straight detail or model lines to split the roof along, then finish.")
                    .Select(r => doc.GetElement(r)).OfType<CurveElement>().ToList();

            var lines = new List<CutLine>();
            foreach (CurveElement ce in chosen)
            {
                if (ce.GeometryCurve is not Line line)
                    throw new InvalidOperationException($"Line {ce.Id} is curved; cut lines must be straight.");
                XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
                if (Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) < 0.00256)
                    throw new InvalidOperationException($"Line {ce.Id} is vertical or too short in plan.");
                lines.Add(new CutLine(ce.Id.ToString(), new Pt(a.X, a.Y), new Pt(b.X, b.Y)));
            }
            if (lines.Count == 0) throw new InvalidOperationException("Select at least one straight line.");
            return lines;
        }

        private sealed class Filter : ISelectionFilter
        {
            private readonly Func<Element, bool> _allow;
            public Filter(Func<Element, bool> allow) { _allow = allow; }
            public bool AllowElement(Element elem) => _allow(elem);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
