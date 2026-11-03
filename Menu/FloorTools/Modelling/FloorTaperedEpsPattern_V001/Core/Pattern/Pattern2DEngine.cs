using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>
    /// Builds the full 2D pattern for a validated convex CCW outline (metres).
    /// Pure geometry: no Revit, no WPF. Never throws for bad input — returns errors.
    /// </summary>
    public static class Pattern2DEngine
    {
        public static Pattern2DResult Generate(Polygon2D? outlineCcw, Pattern2DSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings = settings.Normalized();

            var errors = new List<string>();
            if (outlineCcw == null || outlineCcw.IsEmpty)
            {
                errors.Add("No valid floor outline.");
            }
            else if (!outlineCcw.IsCounterClockwise || !outlineCcw.IsConvex(GeometryTolerance.ParallelSine))
            {
                errors.Add("Floor outline must be convex and counter-clockwise (run OutlineValidator first).");
            }
            else if (settings.GridDirection == GridDirectionMode.SelectedEdge &&
                     (settings.AlignEdgeIndex < 0 || settings.AlignEdgeIndex >= outlineCcw.Count))
            {
                errors.Add($"Alignment edge {settings.AlignEdgeIndex + 1} does not exist on this floor (it has {outlineCcw.Count} edges).");
            }

            errors.AddRange(SettingsValidator.Validate(settings));
            if (errors.Count > 0)
            {
                return Pattern2DResult.Failed(errors);
            }

            Polygon2D outline = outlineCcw!;
            var distances = new EdgeDistanceField(outline);
            var thickness = new ThicknessField(distances, settings.MinThicknessMm, settings.SlopePercent);
            IReadOnlyList<SlopeFace> faces = SlopeFaceBuilder.Build(distances);

            double maxDistance = faces.SelectMany(f => f.Polygon.Vertices)
                                      .Select(distances.MinDistance)
                                      .DefaultIfEmpty(0)
                                      .Max();
            double maxThickness = settings.MinThicknessMm + thickness.K * Math.Max(0, maxDistance);

            var segments = new List<PatternSegment>();
            double minLen = Math.Max(settings.MinSegmentLengthM, GeometryTolerance.Epsilon);
            GridFrame? frame = null;
            IReadOnlyList<BoardPiece> rawBoards;
            int slivers = 0;
            IReadOnlyList<string> notes = Array.Empty<string>();

            if (settings.GridDirection == GridDirectionMode.EachEdge)
            {
                RowLayoutResult rows = RowLayoutBuilder.Build(thickness, faces, settings, minLen);
                rawBoards = rows.Boards; // already coded (F / H-L / H-R / C)
                slivers = rows.SliverCount;
                notes = rows.Notes;
                if (settings.DrawGrid)
                {
                    segments.AddRange(rows.GridLines);
                }
            }
            else
            {
                frame = GridFrame.Create(outline, settings);
                rawBoards = BoardPieceBuilder.Build(frame, thickness, faces);
                if (settings.DrawGrid)
                {
                    segments.AddRange(GridLineBuilder.Build(outline, frame, minLen)
                        .Select(s => new PatternSegment(PatternLayer.Grid, s)));
                }
            }

            IReadOnlyList<BoardPiece> boards = settings.GridDirection == GridDirectionMode.EachEdge
                ? rawBoards
                : BoardCodeAssigner.Assign(rawBoards);

            if (settings.DrawHipRidge)
            {
                segments.AddRange(HipRidgeExtractor.Extract(outline, faces, minLen)
                    .Select(s => new PatternSegment(PatternLayer.HipRidge, s)));
            }

            IReadOnlyList<ContourLoop> contours = settings.DrawContours
                ? ContourBuilder.Build(thickness, settings.EffectiveContourIntervalMm, maxThickness)
                : Array.Empty<ContourLoop>();
            // A contour piece lying exactly on a grid line (common in "each edge" mode, where board
            // rows run parallel to the edges like the contours) is not drawn twice: the grid line stays.
            List<Segment2D> gridLines = segments.Where(s => s.Layer == PatternLayer.Grid).Select(s => s.Segment).ToList();
            int suppressed = 0;
            foreach (ContourLoop loop in contours)
            {
                foreach (Segment2D e in loop.Polygon.Edges.Where(e => e.Length >= minLen))
                {
                    if (gridLines.Any(g => g.SameAs(e, GeometryTolerance.Merge)))
                    {
                        suppressed++;
                        continue;
                    }

                    segments.Add(new PatternSegment(PatternLayer.Contour, e, loop.ThicknessMm));
                }
            }

            (IReadOnlyList<HatchRegion> hatches, IReadOnlyList<LegendEntry> legend) = HatchAssigner.Assign(boards, settings.Hatch);

            return new Pattern2DResult
            {
                Hatches = hatches,
                Legend = legend,
                Outline = outline,
                Frame = frame,
                Faces = faces,
                Boards = boards,
                Contours = contours,
                Segments = segments,
                Labels = BoardLabelBuilder.Build(boards, settings.Labels),
                MaxThicknessMm = maxThickness,
                ContoursOnGridLines = suppressed,
                SliverCount = slivers,
                LayoutNotes = notes
            };
        }
    }
}
