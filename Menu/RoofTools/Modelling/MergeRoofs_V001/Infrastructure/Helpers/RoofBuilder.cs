// Merge Roofs V001 — creates the merged footprint roof: arcs first, then (once) flattened segments.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models;
using Edge = Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models.Edge;
using Revit26_Plugin_Revised.MergeRoofs.V001.Core.Services;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Infrastructure.Helpers
{
    /// <summary>Creates the merged roof in the document.</summary>
    public static class RoofBuilder
    {
        /// <summary>
        /// Creates one footprint roof from <paramref name="merged"/> using the template roof's type and level. Tries once with
        /// arcs re-fitted; if that fails (or there are no arcs to fit) uses the flattened segments. A failed attempt is rolled back.
        /// Throws when the final attempt fails. <paramref name="check"/> (optional) is called between steps; a cancel it raises
        /// as <see cref="OperationCanceledException"/> rolls the attempt back and is never retried.
        /// </summary>
        /// <returns>The new roof and whether arcs were kept.</returns>
        public static (FootPrintRoof Roof, bool UsedArcs) Create(Document doc, FootPrintRoof template, MergeResult merged,
            IReadOnlyList<RoofFootprint> sources, double toleranceFeet, Action? check = null)
        {
            check?.Invoke();
            if (!merged.CanCreate) throw new InvalidOperationException("The roofs are not joined into a single outline.");
            RoofType type = doc.GetElement(template.GetTypeId()) as RoofType
                            ?? throw new InvalidOperationException("The first roof has no roof type.");
            Level level = ResolveLevel(doc, template);

            var flat = merged.Outer.Concat(merged.Holes).ToList();
            var withArcs = flat.Select(l => ArcRefitter.Refit(l, sources, toleranceFeet)).ToList();
            bool hasArcs = withArcs.Any(l => l.Edges.Any(e => e.ArcMid != null));

            if (hasArcs)
            {
                try { return (Attempt(doc, type, level, withArcs, check), true); }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* rolled back inside Attempt; fall through to segments */ }
            }
            check?.Invoke();
            return (Attempt(doc, type, level, flat, check), false);
        }

        private static Level ResolveLevel(Document doc, FootPrintRoof template)
        {
            ElementId id = template.get_Parameter(BuiltInParameter.ROOF_BASE_LEVEL_PARAM)?.AsElementId() ?? template.LevelId;
            return doc.GetElement(id) as Level ?? throw new InvalidOperationException("The first roof has no level.");
        }

        private static FootPrintRoof Attempt(Document doc, RoofType type, Level level, List<Loop> loops, Action? check)
        {
            using var t = new Transaction(doc, "Merge roofs");
            t.Start();
            try
            {
                double z = level.Elevation;
                var curves = new CurveArray();
                var edgeOrder = new List<Edge>();
                foreach (Loop loop in loops)
                    foreach (Edge e in loop.Edges)
                    {
                        check?.Invoke();
                        XYZ a = new(e.A.X, e.A.Y, z), b = new(e.B.X, e.B.Y, z);
                        curves.Append(e.ArcMid is Pt m ? Arc.Create(a, b, new XYZ(m.X, m.Y, z)) : Line.CreateBound(a, b));
                        edgeOrder.Add(e);
                    }
                check?.Invoke();
                FootPrintRoof roof = doc.Create.NewFootPrintRoof(curves, level, type, out ModelCurveArray map)
                                     ?? throw new InvalidOperationException("Revit returned no roof.");
                check?.Invoke();
                int i = 0;
                foreach (ModelCurve mc in map)
                {
                    Edge e = edgeOrder[i++];
                    if (!e.DefinesSlope) continue;
                    roof.set_DefinesSlope(mc, true);
                    roof.set_SlopeAngle(mc, e.SlopeAngle);
                }
                t.Commit();
                return roof;
            }
            catch
            {
                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                throw;
            }
        }
    }
}
