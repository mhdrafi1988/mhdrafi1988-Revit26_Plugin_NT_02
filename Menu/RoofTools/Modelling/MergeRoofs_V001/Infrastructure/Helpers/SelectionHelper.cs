// Merge Roofs V001 — gets the footprint roofs to merge (pre-selection or a pick) and validates them.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers
{
    /// <summary>Selection of the roofs to merge.</summary>
    public static class SelectionHelper
    {
        /// <summary>
        /// Returns the selected footprint roofs (pre-selected ones, otherwise the user picks). Throws
        /// <see cref="InvalidOperationException"/> with a clear message for fewer than two roofs, non-footprint roofs,
        /// or roofs checked out by another user. A cancelled pick propagates as <see cref="Autodesk.Revit.Exceptions.OperationCanceledException"/>.
        /// </summary>
        public static IReadOnlyList<FootPrintRoof> GetRoofs(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds().ToList();
            var chosen = ids.Select(doc.GetElement).Where(e => e is RoofBase).ToList();
            if (chosen.Count < 2)
            {
                var refs = uidoc.Selection.PickObjects(ObjectType.Element, new RoofFilter(),
                    "Select two or more footprint roofs to merge, then finish.");
                chosen = refs.Select(r => doc.GetElement(r)).ToList();
            }

            var roofs = new List<FootPrintRoof>();
            foreach (Element e in chosen)
            {
                if (e is not FootPrintRoof fr)
                    throw new InvalidOperationException($"Roof {e.Id} is not a footprint roof (extrusion roofs are not supported).");
                roofs.Add(fr);
            }
            if (roofs.Count < 2)
                throw new InvalidOperationException("Select at least two footprint roofs.");
            if (doc.IsReadOnly)
                throw new InvalidOperationException("The document is read-only.");
            if (doc.IsWorkshared)
                foreach (var r in roofs)
                    if (WorksharingUtils.GetCheckoutStatus(doc, r.Id) == CheckoutStatus.OwnedByOtherUser)
                        throw new InvalidOperationException($"Roof {r.Id} is checked out by another user.");
            return roofs;
        }

        private sealed class RoofFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is FootPrintRoof;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
