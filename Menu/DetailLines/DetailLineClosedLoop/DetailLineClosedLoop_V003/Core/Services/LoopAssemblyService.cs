using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Geometry;

namespace Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Core.Services
{
    /// <summary>
    /// Step 7 — validates that the processed curve set forms exactly one
    /// simple closed loop (every vertex degree 2, single connected component,
    /// no non-adjacent self-intersections), orders/orients the curves head to
    /// tail, and hands them to CurveLoop.Create().
    /// </summary>
    public static class LoopAssemblyService
    {
        public static bool TryBuildLoop(List<Curve> curves, double topologyEpsilon, out CurveLoop loop, out string error)
        {
            loop = null;
            error = null;

            if (curves.Count < 3)
            {
                error = $"Only {curves.Count} curve(s) remain — at least 3 are required to close a loop.";
                return false;
            }

            EndpointGraph graph = EndpointGraph.Build(curves, topologyEpsilon);

            for (int n = 0; n < graph.Nodes.Count; n++)
            {
                ToolCancellation.CheckCurrent();
                int degree = graph.Degree(n);
                if (degree != 2)
                {
                    XYZ p = graph.Nodes[n];
                    error = $"Curve network is not a simple closed loop — vertex at ({p.X:F2}, {p.Y:F2}, {p.Z:F2}) has {degree} connection(s), expected 2. Increase gap tolerance, enable endpoint snapping, or adjust the selection.";
                    return false;
                }
            }

            if (graph.Nodes.Count != curves.Count)
            {
                error = $"Curve network forms {CountComponents(graph)} disconnected loop(s)/branch(es) instead of one closed loop.";
                return false;
            }

            var used = new bool[curves.Count];
            var ordered = new List<Curve>();

            used[0] = true;
            ordered.Add(OrientCurve(curves[0], graph.Nodes[graph.Edges[0].Start]));
            int startNode = graph.Edges[0].Start;
            int currentNode = graph.Edges[0].End;

            for (int step = 1; step < curves.Count; step++)
            {
                int nextEdge = -1;
                for (int e = 0; e < curves.Count; e++)
                {
                    if (used[e]) continue;
                    if (graph.Edges[e].Start == currentNode || graph.Edges[e].End == currentNode)
                    {
                        nextEdge = e;
                        break;
                    }
                }

                if (nextEdge < 0)
                {
                    error = "Curve network could not be walked into a single continuous loop.";
                    return false;
                }

                used[nextEdge] = true;
                (int s, int en) = graph.Edges[nextEdge];
                int nextNode = s == currentNode ? en : s;
                ordered.Add(OrientCurve(curves[nextEdge], graph.Nodes[currentNode]));
                currentNode = nextNode;
            }

            if (currentNode != startNode)
            {
                error = "Curve loop did not return to its starting point.";
                return false;
            }

            int count = ordered.Count;
            for (int i = 0; i < count; i++)
            {
                ToolCancellation.CheckCurrent();
                for (int j = i + 2; j < count; j++)
                {
                    if (i == 0 && j == count - 1) continue; // adjacent via wraparound
#pragma warning disable CS0618 // see CurveIntersectionService for rationale
                    bool intersects = ordered[i].Intersect(ordered[j], out _) == SetComparisonResult.Overlap;
#pragma warning restore CS0618
                    if (intersects)
                    {
                        error = "Assembled loop is self-intersecting — two non-adjacent curves cross.";
                        return false;
                    }
                }
            }

            try
            {
                loop = CurveLoop.Create(ordered);
            }
            catch (Exception ex)
            {
                error = $"CurveLoop assembly failed: {ex.Message}";
                return false;
            }

            return true;
        }

        private static Curve OrientCurve(Curve c, XYZ from)
        {
            return c.GetEndPoint(0).DistanceTo(from) <= c.GetEndPoint(1).DistanceTo(from)
                ? c
                : c.CreateReversed();
        }

        private static int CountComponents(EndpointGraph graph)
        {
            var visited = new bool[graph.Nodes.Count];
            int components = 0;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (visited[i]) continue;
                components++;

                var stack = new Stack<int>();
                stack.Push(i);
                visited[i] = true;

                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    foreach (var e in graph.Edges)
                    {
                        int other = -1;
                        if (e.Start == cur) other = e.End;
                        else if (e.End == cur) other = e.Start;

                        if (other >= 0 && !visited[other])
                        {
                            visited[other] = true;
                            stack.Push(other);
                        }
                    }
                }
            }

            return components;
        }
    }
}
