using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>Selects elements in Revit from the tool's grids and zooms to them.</summary>
    public static class SelectionService
    {
        /// <summary>
        /// Selects the elements of <paramref name="ids"/> that still exist and, with <paramref name="zoom"/>,
        /// zooms the active view to them. Returns how many were selected.
        /// </summary>
        public static int Select(UIDocument uidoc, IEnumerable<long> ids, bool zoom)
        {
            var doc = uidoc.Document;
            var existing = ids.Distinct().Select(id => new ElementId(id)).Where(id => doc.GetElement(id) != null).ToList();
            uidoc.Selection.SetElementIds(existing);
            if (zoom && existing.Count > 0) uidoc.ShowElements(existing);
            return existing.Count;
        }

        /// <summary>The element ids selected in Revit now.</summary>
        public static HashSet<long> Current(UIDocument uidoc) =>
            new(uidoc.Selection.GetElementIds().Select(id => id.Value));
    }
}
