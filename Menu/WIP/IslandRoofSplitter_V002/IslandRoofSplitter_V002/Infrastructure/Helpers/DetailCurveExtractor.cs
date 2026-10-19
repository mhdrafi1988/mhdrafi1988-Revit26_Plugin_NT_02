// IslandRoofSplitter V002 — DetailCurve → SourceSegment (plan XY only; Z of the sketch plane is ignored).
// Lines and circular arcs keep their exact definition; ellipses/splines use Revit's own tessellation.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.Helpers
{
    public static class DetailCurveExtractor
    {
        public static List<SourceSegment> Extract(IEnumerable<DetailCurve> curves,
                                                  out int lines, out int arcs, out int others)
        {
            lines = arcs = others = 0;
            var list = new List<SourceSegment>();
            foreach (var dc in curves)
            {
                var c = dc.GeometryCurve;
                if (c == null) continue;
                long id = dc.Id.Value;

                if (c is Line ln)
                {
                    lines++;
                    list.Add(new SourceSegment { SourceId = id, Kind = SegmentKind.Line, Points = new List<Pt2> { P(ln.GetEndPoint(0)), P(ln.GetEndPoint(1)) } });
                }
                else if (c is Arc arc)
                {
                    arcs++;
                    List<Pt2> pts = arc.IsBound
                        ? new List<Pt2> { P(arc.GetEndPoint(0)), P(arc.Evaluate(0.5, true)), P(arc.GetEndPoint(1)) }
                        : new List<Pt2>
                        {
                            // full circle: start == end, mid is the opposite point
                            P(arc.Center + arc.XDirection * arc.Radius),
                            P(arc.Center - arc.XDirection * arc.Radius),
                            P(arc.Center + arc.XDirection * arc.Radius)
                        };
                    list.Add(new SourceSegment { SourceId = id, Kind = SegmentKind.Arc, Points = pts });
                }
                else
                {
                    others++;
                    var pts = c.Tessellate().Select(P).ToList();
                    if (pts.Count >= 2)
                        list.Add(new SourceSegment { SourceId = id, Kind = SegmentKind.Polyline, Points = pts });
                }
            }
            return list;
        }

        private static Pt2 P(XYZ p) => new(p.X, p.Y);
    }
}
