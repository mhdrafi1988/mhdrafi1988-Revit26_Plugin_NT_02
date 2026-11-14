using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Joins collinear segments that touch end to end — but only through a vertex where
    /// exactly two segments meet. Where three or more lines meet (e.g. the centre of a
    /// square where four hips meet) the segments stay separate.
    /// </summary>
    public static class SegmentMerger
    {
        public static IReadOnlyList<Segment2D> MergeCollinear(IReadOnlyList<Segment2D> segments, double tolerance)
        {
            var nodes = new List<Vec2>();
            int NodeOf(Vec2 p)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].AlmostEquals(p, tolerance))
                    {
                        return i;
                    }
                }

                nodes.Add(p);
                return nodes.Count - 1;
            }

            var edges = new List<(int A, int B)?>();
            foreach (Segment2D s in segments)
            {
                int a = NodeOf(s.Start);
                int b = NodeOf(s.End);
                if (a != b)
                {
                    edges.Add((a, b));
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                var incidence = new Dictionary<int, List<int>>();
                for (int e = 0; e < edges.Count; e++)
                {
                    if (edges[e] is not (int a, int b))
                    {
                        continue;
                    }

                    Incident(incidence, a).Add(e);
                    Incident(incidence, b).Add(e);
                }

                foreach (KeyValuePair<int, List<int>> kv in incidence)
                {
                    if (kv.Value.Count != 2)
                    {
                        continue;
                    }

                    int node = kv.Key;
                    int e1 = kv.Value[0];
                    int e2 = kv.Value[1];
                    int o1 = Other(edges[e1]!.Value, node);
                    int o2 = Other(edges[e2]!.Value, node);
                    if (o1 == o2)
                    {
                        continue;
                    }

                    Vec2 d1 = (nodes[o1] - nodes[node]).Normalized();
                    Vec2 d2 = (nodes[o2] - nodes[node]).Normalized();
                    if (Math.Abs(d1.Cross(d2)) <= GeometryTolerance.ParallelSine && d1.Dot(d2) < 0)
                    {
                        edges[e1] = (o1, o2);
                        edges[e2] = null;
                        changed = true;
                        break;
                    }
                }
            }

            return edges.Where(e => e.HasValue)
                        .Select(e => new Segment2D(nodes[e!.Value.A], nodes[e.Value.B]))
                        .ToList();
        }

        private static List<int> Incident(Dictionary<int, List<int>> map, int node)
        {
            if (!map.TryGetValue(node, out List<int>? list))
            {
                list = new List<int>();
                map[node] = list;
            }

            return list;
        }

        private static int Other((int A, int B) edge, int node) => edge.A == node ? edge.B : edge.A;
    }
}
