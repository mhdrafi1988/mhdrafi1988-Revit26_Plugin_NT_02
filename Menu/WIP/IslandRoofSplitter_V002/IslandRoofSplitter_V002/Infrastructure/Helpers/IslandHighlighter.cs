// IslandRoofSplitter V002 — selects an island's source detail lines (and created roof, if any) and zooms to them.
// Must run in API context → called from the ExternalEvent handler.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class IslandHighlighter
    {
        public static void Show(UIDocument uidoc, IEnumerable<long> ids)
        {
            var doc = uidoc.Document;
            var list = ids.Select(i => new ElementId(i)).Where(id => doc.GetElement(id) != null).ToList();
            uidoc.Selection.SetElementIds(list);
            if (list.Count > 0) uidoc.ShowElements(list);
        }
    }
}
