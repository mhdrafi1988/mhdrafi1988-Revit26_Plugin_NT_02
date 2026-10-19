// =======================================================
// File: RidgePlanner.cs
// Location: Core/Engine/
// Step 5 (ridge) planning, using Ridge By Openings V070's own services
// unchanged — the same pipeline its window runs:
//   seeds    = the centres (bounding-box centre, as V070) of the openings
//              ticked as drains,
//   grouping = seeds closer than the proximity distance act as one,
//   Voronoi  → ridge lines midway between drain groups, clipped to the
//              roof boundary, with shape points at their vertices and
//              edge midpoints,
//   openings that are not drains end the ridges that cross them,
//   validation checks each ridge point is equidistant from its drains.
// Pure geometry on data read beforehand (RidgeInput), so the window can
// preview it; RidgeCreationService (V070) writes it during the run.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Models;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine
{
    /// <summary>The roof data the ridge step needs, read once by Ridge By Openings V070's readers.</summary>
    public sealed class RidgeInput
    {
        /// <summary>Roof boundary polygon (plan, Z = 0), from V070's RoofBoundaryService.</summary>
        public List<XYZ> Boundary { get; init; } = new List<XYZ>();

        /// <summary>Opening polygons (plan, Z = 0), from V070's InnerLoopService.</summary>
        public List<List<XYZ>> InnerLoops { get; init; } = new List<List<XYZ>>();

        /// <summary>Line styles for the ridge detail lines.</summary>
        public List<string> LineStyles { get; init; } = new List<string>();
    }

    /// <summary>Plans the ridge between the drains (see the file header).</summary>
    public static class RidgePlanner
    {
        /// <summary>Openings whose centres are this close (10 mm) are the same opening.</summary>
        private const double SameCentreFeet = 10.0 / 304.8;

        /// <summary>
        /// Plans the ridge. Returns the V070 result, or null with <paramref name="error"/> set
        /// (fewer than two drain groups, or nothing to clip against).
        /// </summary>
        public static VoronoiRidgeResult Plan(RidgeInput input, IEnumerable<OpeningModel> drainOpenings,
                                              PipelineSettings settings, out string error)
        {
            error = null;
            if (input == null || input.Boundary.Count < 3)
            {
                error = "the roof boundary could not be read for the ridge.";
                return null;
            }

            List<XYZ> seeds = (drainOpenings ?? Enumerable.Empty<OpeningModel>())
                .Where(o => o?.Loop != null)
                .Select(o =>
                {
                    var (min, max) = o.Loop.Bounds;
                    return new XYZ((min.X + max.X) / 2, (min.Y + max.Y) / 2, 0);
                })
                .ToList();
            if (seeds.Count < 2)
            {
                error = "tick at least two openings as drains — ridges run between drains.";
                return null;
            }

            var groups = new DrainGroupingService()
                .GroupDrains(seeds, DrainGroupingService.MmToFeet(Math.Max(1, settings.RidgeProximityMm)));
            if (groups.Count < 2)
            {
                error = $"the drains are all within {settings.RidgeProximityMm:0} mm of each other, so they form one group and there is no ridge between them.";
                return null;
            }

            var result = new VoronoiRidgeResult
            {
                BoundaryPolygon = input.Boundary,
                RoofCentroid = new XYZ(input.Boundary.Average(p => p.X), input.Boundary.Average(p => p.Y), 0),
                InnerLoops = input.InnerLoops,
                DrainGroups = groups
            };

            new VoronoiComputationService().Compute(groups, result);
            new VoronoiClippingService().ClipAndCollect(result, input.Boundary, groups);
            result.RidgeEdgeMidPoints = result.ClippedEdges
                .Select(e => new XYZ((e.Start.X + e.End.X) / 2.0, (e.Start.Y + e.End.Y) / 2.0, 0.0))
                .ToList();

            // Openings that are not drains end the ridges that cross them (V070's "unselected loops").
            var stoppers = input.InnerLoops.Where(loop => !seeds.Any(s => BoxCentre(loop).DistanceTo(s) <= SameCentreFeet)).ToList();
            if (stoppers.Count > 0)
                new InnerLoopIntersectionService().ComputeInnerLoopIntersections(result, input.Boundary, stoppers);

            var validation = new RidgeValidationService { ToleranceFeet = DrainGroupingService.MmToFeet(Math.Max(1, settings.RidgeValidationToleranceMm)) };
            validation.Validate(result, groups);
            return result;
        }

        private static XYZ BoxCentre(List<XYZ> loop)
        {
            if (loop == null || loop.Count == 0) return XYZ.Zero;
            return new XYZ((loop.Min(p => p.X) + loop.Max(p => p.X)) / 2, (loop.Min(p => p.Y) + loop.Max(p => p.Y)) / 2, 0);
        }
    }
}
