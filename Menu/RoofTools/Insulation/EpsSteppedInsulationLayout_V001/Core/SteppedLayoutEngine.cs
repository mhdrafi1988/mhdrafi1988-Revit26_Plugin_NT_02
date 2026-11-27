using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core
{
    /// <summary>
    /// Computes the stepped EPS insulation grid: aligns a 1200x1200 grid to a chosen edge,
    /// finds drain distances for each cell, and calculates how many 35 mm layers stack on the
    /// 100 mm base.
    /// </summary>
    public static class SteppedLayoutEngine
    {
        /// <summary>
        /// Computes the layout. <paramref name="outlineMetres"/> is the roof boundary (CCW, metres).
        /// <paramref name="drains"/> are the detected drain points (metres).
        /// </summary>
        public static LayoutResult Compute(
            IReadOnlyList<Vec2> outlineMetres,
            IReadOnlyList<DrainPoint> drains,
            InsulationSettings settings)
        {
            var errors = new List<string>();

            if (outlineMetres == null || outlineMetres.Count < 3)
                errors.Add("The roof outline is invalid (fewer than 3 vertices).");
            if (drains == null || drains.Count == 0)
                errors.Add("No drain points detected. The roof must have shape editing applied.");
            if (settings.SlopePercent <= 0)
                errors.Add("Slope must be greater than zero.");

            if (errors.Count > 0)
                return new LayoutResult { Errors = errors, Cells = Array.Empty<InsulationCell>(), PanelsPerLayer = Array.Empty<int>() };

            // Grid direction from selected edge
            int ei = settings.AlignEdgeIndex;
            if (ei < 0 || ei >= outlineMetres.Count) ei = LongestEdgeIndex(outlineMetres);
            Vec2 edgeDir = EdgeDirection(outlineMetres, ei);

            // Build local frame
            Vec2 u = edgeDir;
            Vec2 v = u.PerpLeft;
            double boardM = settings.BoardSizeM;

            // Project outline to local
            double uMin = double.MaxValue, uMax = double.MinValue;
            double vMin = double.MaxValue, vMax = double.MinValue;
            foreach (Vec2 pt in outlineMetres)
            {
                double pu = pt.Dot(u);
                double pv = pt.Dot(v);
                if (pu < uMin) uMin = pu;
                if (pu > uMax) uMax = pu;
                if (pv < vMin) vMin = pv;
                if (pv > vMax) vMax = pv;
            }

            double slopeMmPerM = settings.SlopePercent * 10.0; // mm rise per metre run
            double minCutM = settings.MinCutSizeMm / 1000.0;

            // Apply grid offset along U axis
            double offsetU = settings.GridOffsetM % boardM;
            double gridUStart = uMin + offsetU;
            if (gridUStart > uMin + boardM) gridUStart -= boardM;

            var cells = new List<InsulationCell>();
            int skipped = 0;
            int serial = 1;

            int col = 0;
            for (double cu = gridUStart; cu < uMax - 1e-6; cu += boardM)
            {
                col++;
                int row = 0;
                for (double cv = vMin; cv < vMax - 1e-6; cv += boardM)
                {
                    row++;
                    double cellUEnd = Math.Min(cu + boardM, uMax);
                    double cellVEnd = Math.Min(cv + boardM, vMax);

                    Vec2 centre = u * ((cu + cellUEnd) / 2.0) + v * ((cv + cellVEnd) / 2.0);

                    // Cell corners in world
                    Vec2 c0 = u * cu + v * cv;
                    Vec2 c1 = u * cellUEnd + v * cv;
                    Vec2 c2 = u * cellUEnd + v * cellVEnd;
                    Vec2 c3 = u * cu + v * cellVEnd;
                    var corners = new[] { c0, c1, c2, c3 };

                    // Check if centre is inside the outline
                    if (!PointInPolygon(centre, outlineMetres))
                        continue;

                    // Trim check: is any corner outside?
                    bool trimmed = corners.Any(c => !PointInPolygon(c, outlineMetres));

                    // Min cut size filter: skip trimmed cells whose smallest dimension is below threshold
                    if (trimmed && minCutM > 0)
                    {
                        var clipped = ClipToOutline(corners, outlineMetres);
                        if (clipped.Count < 3 || MinDimension(clipped) < minCutM)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    // Distance from centre to nearest drain
                    double minDist = double.MaxValue;
                    foreach (DrainPoint dp in drains)
                    {
                        double d = centre.DistanceTo(dp.Position);
                        if (d < minDist) minDist = d;
                    }

                    double riseAboveDrainMm = minDist * slopeMmPerM;
                    int stepLayers = (int)Math.Ceiling(riseAboveDrainMm / settings.StepThicknessMm);
                    if (stepLayers < 0) stepLayers = 0;

                    double totalHeight = settings.BaseThicknessMm + stepLayers * settings.StepThicknessMm;

                    cells.Add(new InsulationCell
                    {
                        Column = col,
                        Row = row,
                        Centre = centre,
                        Boundary = trimmed ? ClipToOutline(corners, outlineMetres) : corners,
                        DrainDistanceM = minDist,
                        StepLayerCount = stepLayers,
                        TotalHeightMm = totalHeight,
                        IsTrimmed = trimmed,
                        Serial = $"EPS_{serial:D4}"
                    });
                    serial++;
                }
            }

            if (cells.Count == 0)
            {
                errors.Add("No cells overlap the roof outline.");
                return new LayoutResult { Errors = errors, Cells = cells, PanelsPerLayer = Array.Empty<int>() };
            }

            int maxSteps = cells.Max(c => c.StepLayerCount);

            // Panels per layer
            var panelsPerLayer = new List<int>();
            panelsPerLayer.Add(cells.Count); // base layer
            for (int layer = 1; layer <= maxSteps; layer++)
            {
                int count = cells.Count(c => c.StepLayerCount >= layer);
                panelsPerLayer.Add(count);
            }

            int totalPanels = panelsPerLayer.Sum();
            double boardAreaM2 = settings.BoardSizeM * settings.BoardSizeM;
            double totalAreaM2 = cells.Count * boardAreaM2;

            double totalVolumeM3 = 0;
            foreach (var cell in cells)
            {
                double thicknessM = cell.TotalHeightMm / 1000.0;
                totalVolumeM3 += boardAreaM2 * thicknessM;
            }

            return new LayoutResult
            {
                Cells = cells,
                TotalCells = cells.Count,
                FullCells = cells.Count(c => !c.IsTrimmed),
                TrimmedCells = cells.Count(c => c.IsTrimmed),
                SkippedCells = skipped,
                MaxStepLayers = maxSteps,
                PanelsPerLayer = panelsPerLayer,
                TotalPanels = totalPanels,
                TotalAreaM2 = totalAreaM2,
                TotalVolumeM3 = totalVolumeM3,
                Errors = Array.Empty<string>()
            };
        }

        /// <summary>Returns the available outline edges with their lengths, for the alignment picker.</summary>
        public static IReadOnlyList<(int Index, double LengthM, Vec2 Start, Vec2 End)> GetEdges(IReadOnlyList<Vec2> outline)
        {
            var edges = new List<(int, double, Vec2, Vec2)>();
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 a = outline[i];
                Vec2 b = outline[(i + 1) % outline.Count];
                double len = a.DistanceTo(b);
                edges.Add((i, len, a, b));
            }
            return edges;
        }

        /// <summary>Index of the longest outline edge.</summary>
        public static int LongestEdgeIndex(IReadOnlyList<Vec2> outline)
        {
            int best = 0;
            double bestLen = 0;
            for (int i = 0; i < outline.Count; i++)
            {
                double len = outline[i].DistanceTo(outline[(i + 1) % outline.Count]);
                if (len > bestLen) { bestLen = len; best = i; }
            }
            return best;
        }

        private static Vec2 EdgeDirection(IReadOnlyList<Vec2> outline, int index)
        {
            Vec2 a = outline[index];
            Vec2 b = outline[(index + 1) % outline.Count];
            return (b - a).Normalized();
        }

        /// <summary>Ray-casting point-in-polygon test.</summary>
        public static bool PointInPolygon(Vec2 p, IReadOnlyList<Vec2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vec2 vi = polygon[i], vj = polygon[j];
                if ((vi.Y > p.Y) != (vj.Y > p.Y) &&
                    p.X < (vj.X - vi.X) * (p.Y - vi.Y) / (vj.Y - vi.Y) + vi.X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Clips a rectangular cell to the outline. Simple approach: keeps only corners inside, adds intersection points.</summary>
        private static IReadOnlyList<Vec2> ClipToOutline(Vec2[] cellCorners, IReadOnlyList<Vec2> outline)
        {
            // Sutherland-Hodgman clipping
            IReadOnlyList<Vec2> subject = cellCorners;
            int n = outline.Count;
            for (int i = 0; i < n; i++)
            {
                if (subject.Count == 0) break;

                Vec2 edgeA = outline[i];
                Vec2 edgeB = outline[(i + 1) % n];
                Vec2 edgeNormal = (edgeB - edgeA).PerpLeft;

                var output = new List<Vec2>();
                for (int j = 0; j < subject.Count; j++)
                {
                    Vec2 current = subject[j];
                    Vec2 prev = subject[(j + subject.Count - 1) % subject.Count];

                    double currDot = edgeNormal.Dot(current - edgeA);
                    double prevDot = edgeNormal.Dot(prev - edgeA);

                    bool currInside = currDot >= -1e-9;
                    bool prevInside = prevDot >= -1e-9;

                    if (currInside)
                    {
                        if (!prevInside)
                            output.Add(Intersect(prev, current, edgeA, edgeB));
                        output.Add(current);
                    }
                    else if (prevInside)
                    {
                        output.Add(Intersect(prev, current, edgeA, edgeB));
                    }
                }

                subject = output;
            }

            return subject;
        }

        /// <summary>Approximate smallest dimension of a convex polygon (min edge-to-opposite-vertex distance).</summary>
        private static double MinDimension(IReadOnlyList<Vec2> poly)
        {
            double min = double.MaxValue;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vec2 a = poly[i];
                Vec2 b = poly[(i + 1) % n];
                Vec2 edge = b - a;
                double edgeLen = Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);
                if (edgeLen < 1e-12) continue;
                Vec2 normal = new Vec2(-edge.Y / edgeLen, edge.X / edgeLen);

                double maxDist = 0;
                for (int j = 0; j < n; j++)
                {
                    double d = Math.Abs(normal.Dot(poly[j] - a));
                    if (d > maxDist) maxDist = d;
                }
                if (maxDist < min) min = maxDist;
            }
            return min;
        }

        private static Vec2 Intersect(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            Vec2 ab = b - a;
            Vec2 cd = d - c;
            double denom = ab.X * cd.Y - ab.Y * cd.X;
            if (Math.Abs(denom) < 1e-15) return a;
            double t = ((c.X - a.X) * cd.Y - (c.Y - a.Y) * cd.X) / denom;
            return a + ab * t;
        }
    }
}
