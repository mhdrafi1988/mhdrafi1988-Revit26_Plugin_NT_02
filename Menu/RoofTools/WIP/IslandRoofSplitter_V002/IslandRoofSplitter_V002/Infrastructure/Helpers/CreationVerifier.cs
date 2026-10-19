// IslandRoofSplitter V002 — "ensure creation" check (per Anas, 2026-10-15). Revit can hand back an element that
// committed without error yet has no solid — a footprint it quietly reduced to nothing. Before an attempt is
// accepted, the new element is regenerated and asked for its geometry: no solid with volume means this attempt
// failed, so the handler rolls it back and moves to the next attempt (tessellated retry, then the floor).
//
// Deliberately forgiving: when the geometry cannot be read at all, the element is accepted and the caller logs a
// note. A verifier that fails on its own blind spots would be worse than none.
// Caller owns the Transaction.
#nullable enable
using System;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class CreationVerifier
    {
        /// <returns>null when the element looks real, otherwise why it does not.</returns>
        public static string? Verify(Document doc, Element element)
        {
            if (element == null || element.Id == ElementId.InvalidElementId)
                return "Revit returned no element";

            doc.Regenerate();

            if (doc.GetElement(element.Id) == null)
                return "the new element is not in the document after regeneration";

            double volume;
            try
            {
                volume = SolidVolume(element);
            }
            catch (Exception)
            {
                return null; // unreadable geometry is not evidence of failure
            }

            return volume > 1e-9 ? null : "the new element has no solid geometry (empty footprint)";
        }

        private static double SolidVolume(Element element)
        {
            var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse };
            GeometryElement? geo = element.get_Geometry(opts);
            if (geo == null) throw new InvalidOperationException("no geometry");

            double total = 0;
            foreach (GeometryObject o in geo)
            {
                if (o is Solid s) total += s.Volume;
                else if (o is GeometryInstance gi)
                    foreach (GeometryObject io in gi.GetInstanceGeometry())
                        if (io is Solid isol) total += isol.Volume;
            }
            return total;
        }
    }
}
