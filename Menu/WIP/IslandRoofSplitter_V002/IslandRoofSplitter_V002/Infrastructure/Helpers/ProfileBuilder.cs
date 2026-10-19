// IslandRoofSplitter V002 — turns an island's traced loops into the two shapes Revit wants: a CurveArray
// (NewFootPrintRoof) and a list of CurveLoops (Floor.Create). One builder for both, so a roof attempt and a
// floor attempt always use the same geometry.
//
// New in V002 (per Anas, 2026-10-15 — "ensure roof creation"): the sketch is sanitised before Revit sees it,
// which is where most NewFootPrintRoof failures come from:
//   * each edge starts exactly where the previous one ended (kills sub-tolerance gaps from Z sampling),
//   * edges shorter than ShortCurveTolerance are dropped instead of thrown at Revit,
//   * the loop is explicitly closed back onto its first point,
//   * tessellate: true replaces every arc with its chords (Revit's own Arc.Tessellate), the retry used when
//     the true-arc attempt fails.
// Caller owns the Transaction. Nothing here touches the document.
#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class ProfileBuilder
    {
        /// <summary>Footprint for NewFootPrintRoof: outer loop first, then one loop per hole.</summary>
        public static CurveArray BuildCurveArray(Island island, double z, bool tessellate, double shortCurveTolFt)
        {
            var arr = new CurveArray();
            foreach (var loop in Loops(island))
                foreach (var c in LoopCurves(loop, z, tessellate, shortCurveTolFt))
                    arr.Append(c);
            return arr;
        }

        /// <summary>Profile for Floor.Create: outer loop first, then one CurveLoop per opening.</summary>
        public static IList<CurveLoop> BuildCurveLoops(Island island, double z, bool tessellate, double shortCurveTolFt)
        {
            var loops = new List<CurveLoop>();
            foreach (var loop in Loops(island))
            {
                var cl = new CurveLoop();
                foreach (var c in LoopCurves(loop, z, tessellate, shortCurveTolFt)) cl.Append(c);
                loops.Add(cl);
            }
            return loops;
        }

        /// <returns>null when the island can be sketched, otherwise why it cannot.</returns>
        public static string? Validate(Island island, double shortCurveTolFt)
        {
            if (island.Outer.Edges.Count == 0) return "no boundary edges";
            foreach (var loop in Loops(island))
            {
                int kept = CountKeptEdges(loop, shortCurveTolFt);
                if (kept < 3) return $"a loop has only {kept} usable edge(s) after dropping sub-tolerance segments";
            }
            return null;
        }

        private static IEnumerable<IslandLoop> Loops(Island island)
        {
            yield return island.Outer;
            foreach (var h in island.Holes) yield return h;
        }

        private static int CountKeptEdges(IslandLoop loop, double shortCurveTolFt)
        {
            int n = 0;
            Pt2? cursor = null;
            foreach (var e in loop.Edges)
            {
                Pt2 s = cursor ?? e.Start;
                if (s.DistanceTo(e.End) <= shortCurveTolFt) continue;
                cursor = e.End;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Sanitised curves for one loop. Each curve starts at the previous curve's exact end point, segments at
        /// or below ShortCurveTolerance are skipped, and the loop is closed back onto its first point.
        /// </summary>
        private static List<Curve> LoopCurves(IslandLoop loop, double z, bool tessellate, double shortCurveTolFt)
        {
            var curves = new List<Curve>();
            if (loop.Edges.Count == 0) return curves;

            Pt2 first = loop.Edges[0].Start;
            Pt2 cursor = first;

            foreach (var e in loop.Edges)
            {
                Pt2 end = e.End;
                if (cursor.DistanceTo(end) <= shortCurveTolFt) continue; // sub-tolerance segment: skip it

                if (e.Mid is Pt2 mid && !tessellate)
                {
                    Arc? arc = TryArc(cursor, end, mid, z);
                    if (arc != null) curves.Add(arc);
                    else curves.Add(Line.CreateBound(P(cursor, z), P(end, z))); // degenerate arc → chord
                }
                else if (e.Mid is Pt2 m2)
                {
                    AppendTessellatedArc(curves, cursor, end, m2, z, shortCurveTolFt);
                }
                else
                {
                    curves.Add(Line.CreateBound(P(cursor, z), P(end, z)));
                }

                cursor = end;
            }

            // close the loop explicitly; Revit rejects an open sketch
            if (cursor.DistanceTo(first) > shortCurveTolFt)
                curves.Add(Line.CreateBound(P(cursor, z), P(first, z)));

            return curves;
        }

        private static void AppendTessellatedArc(List<Curve> curves, Pt2 start, Pt2 end, Pt2 mid, double z,
                                                 double shortCurveTolFt)
        {
            Arc? arc = TryArc(start, end, mid, z);
            if (arc == null)
            {
                curves.Add(Line.CreateBound(P(start, z), P(end, z)));
                return;
            }

            IList<XYZ> pts = arc.Tessellate();
            if (pts == null || pts.Count < 2)
            {
                curves.Add(Line.CreateBound(P(start, z), P(end, z)));
                return;
            }

            XYZ prev = P(start, z); // keep the exact cursor as the first point
            for (int i = 1; i < pts.Count; i++)
            {
                XYZ next = i == pts.Count - 1 ? P(end, z) : new XYZ(pts[i].X, pts[i].Y, z);
                if (prev.DistanceTo(next) <= shortCurveTolFt) continue;
                curves.Add(Line.CreateBound(prev, next));
                prev = next;
            }
            if (prev.DistanceTo(P(end, z)) > shortCurveTolFt)
                curves.Add(Line.CreateBound(prev, P(end, z)));
        }

        private static Arc? TryArc(Pt2 start, Pt2 end, Pt2 mid, double z)
        {
            try
            {
                return Arc.Create(P(start, z), P(end, z), P(mid, z));
            }
            catch (Exception)
            {
                return null; // collinear / coincident three points
            }
        }

        private static XYZ P(Pt2 p, double z) => new XYZ(p.X, p.Y, z);
    }
}
