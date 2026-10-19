// IslandRoofSplitter V002 — the whole dry run, pure Core (no Revit API, no ExternalEvent):
// tessellate → heal ≤1 mm → planar graph → drop open chains / bridges → trace islands → validate →
// sample Z on the parent roof → build sketch edges → name Island_01… in plan reading order.
// Re-run on every option change; it's cheap compared to anything that touches the document.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Core.Services
{
    public static class IslandPreviewService
    {
        /// <param name="shortCurveTolFt">Revit Application.ShortCurveTolerance, captured by the Command.</param>
        public static PreviewResult Run(IReadOnlyList<SourceSegment> segments, RoofSurfaceMesh mesh,
                                        IslandSplitterSettings settings, double shortCurveTolFt)
        {
            var s = settings.Clone().Clamp();
            var result = new PreviewResult();
            double healTol = LengthUnits.MmToFt(IslandSplitterSettings.HealToleranceMm);
            double chordTol = LengthUnits.MmToFt(s.ChordToleranceMm);

            // Sloped mode always tessellates; Flat keeps true arcs only when asked
            bool keepTrueArcs = s.Mode == SplitMode.Flat && s.Arcs == ArcOutput.TrueArcs;

            var arcs = new List<ArcInfo>();
            var tess = CurveTessellator.Tessellate(segments, chordTol, arcs);
            result.HealedEndpoints = EndpointHealer.Heal(tess, healTol);

            var graph = PlanarGraphBuilder.Build(tess, arcs, s.Crossings, healTol);
            var open = GraphCleaner.RemoveDangling(graph);
            result.IgnoredConnectorLines = GraphCleaner.RemoveBridges(graph);
            // a removed bridge can leave nothing dangling, but run once more for safety (not reported)
            GraphCleaner.RemoveDangling(graph);

            var raw = IslandFinder.Find(graph, s.Nesting);
            var zs = new SurfaceZSampler(mesh, LengthUnits.MmToFt(1));
            bool sloped = s.Mode == SplitMode.Sloped;
            bool addCreases = sloped && s.Interior == InteriorPoints.RidgeValleyPoints;
            double spacing = Math.Max(shortCurveTolFt * 2, LengthUnits.MmToFt(5));

            var islands = new List<Island>();
            foreach (var r in raw)
                islands.Add(BuildIsland(r, arcs, zs, sloped, keepTrueArcs, addCreases, shortCurveTolFt, spacing));

            foreach (var ch in open)
                islands.Add(BuildSkippedChain(ch, graph));

            // detection order = plan reading order: top band first, then left to right
            double top = islands.Count > 0 ? islands.Max(i => i.MaxY) : 0;
            const double bandFt = 1.0;
            var ordered = islands
                .OrderBy(i => Math.Floor((top - i.MaxY) / bandFt))
                .ThenBy(i => i.MinX)
                .ThenBy(i => i.MinY)
                .ToList();
            for (int k = 0; k < ordered.Count; k++)
            {
                ordered[k].Number = k + 1;
                ordered[k].Name = $"Island_{k + 1:D2}";
            }
            result.Islands.AddRange(ordered);

            // messages
            result.Messages.Add(new(MessageSeverity.Info,
                $"Preview: {segments.Count} detail curves, mode {s.Mode}, nesting {s.Nesting}, crossings {(s.Crossings == CrossingMode.Split ? "split" : "chains only")}, chord {s.ChordToleranceMm:0.#} mm"));
            if (result.HealedEndpoints > 0)
                result.Messages.Add(new(MessageSeverity.Info, $"Healed {result.HealedEndpoints} endpoint(s) within 1 mm"));
            if (result.IgnoredConnectorLines > 0)
                result.Messages.Add(new(MessageSeverity.Warning, $"Ignored {result.IgnoredConnectorLines} connector segment(s) that join loops but enclose nothing"));
            if (zs.IsEmpty)
                result.Messages.Add(new(MessageSeverity.Warning, "Source roof has no top faces to sample — Z unavailable"));
            foreach (var i in result.Islands)
            {
                string z = i.ZMin.HasValue ? $"Z {LengthUnits.FtToMm(i.ZMin.Value):0}–{LengthUnits.FtToMm(i.ZMax!.Value):0} mm" : "Z n/a";
                var sev = i.Status switch
                {
                    IslandStatus.Skipped => MessageSeverity.Warning,
                    IslandStatus.Failed => MessageSeverity.Error,
                    _ => MessageSeverity.Debug
                };
                result.Messages.Add(new(sev, $"{i.Name}  {i.Status}  {i.VertexCount} vertices  {z}{(string.IsNullOrEmpty(i.Reason) ? "" : "  " + i.Reason)}"));
            }
            result.Messages.Add(new(MessageSeverity.Info,
                $"Preview: {result.IslandsDetected} islands, {result.ReadyCount} ready, {result.SkippedCount} skipped" +
                $"{(result.Islands.Any(i => i.Status == IslandStatus.Failed) ? $", {result.Islands.Count(i => i.Status == IslandStatus.Failed)} will fail" : "")}. Nothing created yet."));
            return result;
        }

        private static Island BuildIsland(RawIsland r, List<ArcInfo> arcs, SurfaceZSampler zs, bool sloped,
                                          bool keepTrueArcs, bool addCreases, double shortTol, double spacing)
        {
            var isl = new Island();
            foreach (var id in r.SourceIds) isl.SourceIds.Add(id);
            SetBox(isl, r.Outer.Points.Concat(r.Holes.SelectMany(h => h.Points)));

            var outer = LoopValidator.Clean(r.Outer, shortTol, out string why);
            if (outer == null) return Skip(isl, why, r.Outer.Vertices.Count);

            var holes = new List<List<LoopVertex>>();
            foreach (var h in r.Holes)
            {
                var hc = LoopValidator.Clean(h, shortTol, out string hw);
                if (hc == null) return Skip(isl, "Opening: " + hw, r.Outer.Vertices.Count);
                holes.Add(hc);
            }

            if (addCreases)
            {
                outer = InteriorPointSampler.InsertCreaseCrossings(outer, zs, spacing);
                for (int k = 0; k < holes.Count; k++) holes[k] = InteriorPointSampler.InsertCreaseCrossings(holes[k], zs, spacing);
            }

            isl.Outer = MakeLoop(outer, arcs, zs, keepTrueArcs, shortTol);
            foreach (var h in holes) isl.Holes.Add(MakeLoop(h, arcs, zs, keepTrueArcs, shortTol));
            isl.AreaSqFt = Math.Abs(Geometry2D.SignedArea(outer.Select(v => v.P).ToList()))
                           - holes.Sum(h => Math.Abs(Geometry2D.SignedArea(h.Select(v => v.P).ToList())));

            if (addCreases)
                isl.InteriorPoints.AddRange(InteriorPointSampler.InsidePoints(
                    outer.Select(v => v.P).ToList(),
                    holes.Select(h => (IReadOnlyList<Pt2>)h.Select(v => v.P).ToList()).ToList(),
                    zs, spacing));

            var allZ = isl.Outer.Z.Concat(isl.Holes.SelectMany(h => h.Z)).ToList();
            var found = allZ.Where(z => z.HasValue).Select(z => z!.Value)
                            .Concat(isl.InteriorPoints.Select(p => p.Z)).ToList();
            if (found.Count > 0) { isl.ZMin = found.Min(); isl.ZMax = found.Max(); }

            isl.VertexCount = isl.Outer.Edges.Count + isl.Holes.Sum(h => h.Edges.Count) + isl.InteriorPoints.Count;

            int misses = allZ.Count(z => !z.HasValue);
            var notes = new List<string>();
            if (!string.IsNullOrEmpty(r.Note)) notes.Add(r.Note);
            int arcCount = isl.Outer.Edges.Count(e => e.IsArc) + isl.Holes.Sum(h => h.Edges.Count(e => e.IsArc));
            if (arcCount > 0) notes.Add(arcCount == 1 ? "1 true arc kept" : $"{arcCount} true arcs kept");
            else
            {
                int tessArcs = outer.Concat(holes.SelectMany(h => h)).Where(v => v.ArcIndex >= 0)
                                    .Select(v => v.ArcIndex).Distinct().Count();
                if (tessArcs > 0) notes.Add($"{tessArcs} arc(s) tessellated");
            }
            if (isl.InteriorPoints.Count > 0) notes.Add($"{isl.InteriorPoints.Count} ridge/valley point(s) inside");

            if (misses > 0)
            {
                string miss = $"Ray-cast missed roof face at {misses} of {allZ.Count} vertices";
                if (sloped) { isl.Status = IslandStatus.Failed; isl.Reason = miss; return isl; }
                notes.Add(miss + " (ignored in Flat mode)");
            }
            isl.Status = IslandStatus.Ready;
            isl.Reason = string.Join(" · ", notes);
            return isl;
        }

        private static IslandLoop MakeLoop(List<LoopVertex> v, List<ArcInfo> arcs, SurfaceZSampler zs,
                                           bool keepTrueArcs, double shortTol)
        {
            var loop = new IslandLoop { Vertices = v };
            loop.Edges = ArcReassembler.Build(v, arcs, keepTrueArcs, shortTol);
            loop.Z = loop.Edges.Select(e => zs.Sample(e.Start)).ToList();
            return loop;
        }

        private static Island Skip(Island isl, string reason, int verts)
        {
            isl.Status = IslandStatus.Skipped;
            isl.Reason = reason;
            isl.VertexCount = verts;
            return isl;
        }

        private static Island BuildSkippedChain(OpenChain ch, PlanarGraph g)
        {
            var isl = new Island { Status = IslandStatus.Skipped };
            foreach (var id in ch.SourceIds) isl.SourceIds.Add(id);
            var pts = ch.EdgeIds.SelectMany(e => new[] { g.Vertices[g.Edges[e].A], g.Vertices[g.Edges[e].B] }).ToList();
            SetBox(isl, pts);
            isl.VertexCount = pts.Distinct().Count();
            double chainLen = ch.EdgeIds.Sum(e => g.Vertices[g.Edges[e].A].DistanceTo(g.Vertices[g.Edges[e].B]));
            if (ch.OpenEnds.Count == 1)
                isl.Reason = "Unclosed: dangling line attached to a loop (one open end)";
            else if (double.IsInfinity(ch.GapFt) || ch.GapFt > 0.5 * chainLen)
                isl.Reason = "Unclosed: open line chain, not part of any loop";
            else
                isl.Reason = $"Unclosed: gap {LengthUnits.FtToMm(ch.GapFt):0.0} mm at {LoopValidator.Fmt(ch.GapAt)} exceeds 1 mm healing";
            return isl;
        }

        private static void SetBox(Island isl, IEnumerable<Pt2> pts)
        {
            var list = pts.ToList();
            if (list.Count == 0) return;
            isl.MinX = list.Min(p => p.X); isl.MaxX = list.Max(p => p.X);
            isl.MinY = list.Min(p => p.Y); isl.MaxY = list.Max(p => p.Y);
        }
    }
}
