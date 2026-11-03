using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Builds the Planned Sections preview list from a set of pre-selected floors:
    /// buckets edges (FloorEdgeBucketingEngine), builds names via the token-based
    /// SectionNamingService (auto-deduping with a D-series suffix — never skips),
    /// then runs a proximity-merge pass across ALL floors' Ready rows and produces
    /// one PlannedSection row per direction per floor (Ready / NoEdgeFound / MergedOut).
    /// </summary>
    public class SectionPlanBuilder
    {
        /// <summary>
        /// Result of a plan-build pass: the rows for the table, plus the
        /// detected/suggested counts needed for the Selection Summary metrics.
        /// </summary>
        public class PlanBuildResult
        {
            public ObservableCollection<PlannedSection> Plan { get; set; }
            public int TotalFloorsCount { get; set; }
            public int DetectedCount { get; set; }
            public int SuggestedCount { get; set; }
        }

        public PlanBuildResult BuildPlan(
            Document doc,
            IList<Element> selectedFloors,
            IList<Element> skippedNonFloorElements,
            double viewRotationRadians,
            FloorEdgeSectionsSettings settings,
            ObservableCollection<LogEntry> log)
        {
            var plan = new ObservableCollection<PlannedSection>();

            log.Add(new LogEntry(LogLevel.Info, $"Selection captured: {selectedFloors.Count + skippedNonFloorElements.Count} elements."));
            if (skippedNonFloorElements.Count > 0)
            {
                log.Add(new LogEntry(LogLevel.Warning,
                    $"Skipped {skippedNonFloorElements.Count} non-floor elements ({string.Join(", ", skippedNonFloorElements.Select(e => e.Category?.Name ?? "Unknown").Distinct())})."));
            }

            if (selectedFloors.Count == 0)
            {
                log.Add(new LogEntry(LogLevel.Warning, "No floor elements in selection — nothing to plan."));
                return new PlanBuildResult { Plan = plan, TotalFloorsCount = 0, DetectedCount = 0, SuggestedCount = 0 };
            }

            HashSet<string> existingViewNames = SectionNamingService.GetExistingViewNames(doc);

            log.Add(new LogEntry(LogLevel.Info, $"Bucketing edges by view-aligned bounding box for {selectedFloors.Count} floors."));

            foreach (Element el in selectedFloors)
            {
                if (el is not Floor floor)
                {
                    // Defensive — should already be filtered before this point.
                    log.Add(new LogEntry(LogLevel.Warning, $"Element {el.Id.Value} is not a Floor — skipped."));
                    continue;
                }

                BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
                if (bbox == null)
                {
                    log.Add(new LogEntry(LogLevel.Warning, $"Floor {floor.Id.Value}: no bounding box available — skipped entirely."));
                    continue;
                }

                string floorDisplayName = SectionNamingService.GetFloorDisplayName(floor);

                var bucketed = FloorEdgeBucketingEngine.BucketEdges(floor, bbox, viewRotationRadians, log);

                foreach (EdgeDirection dir in Enum.GetValues(typeof(EdgeDirection)).Cast<EdgeDirection>())
                {
                    if (!bucketed.TryGetValue(dir, out var edge))
                    {
                        plan.Add(new PlannedSection
                        {
                            FloorId = floor.Id,
                            FloorDisplayName = floorDisplayName,
                            Direction = dir,
                            SectionViewName = "—",
                            EdgeLengthMm = 0,
                            Status = PlannedSectionStatus.NoEdgeFound,
                            IsIncluded = false,
                            FloorBoundingBox = bbox
                        });
                        continue;
                    }

                    // Geometry only here — naming is deferred until after the proximity-merge
                    // pass below, so merged-out rows never consume a Number-sequence slot or
                    // an entry in existingViewNames for a view that will never be created.
                    plan.Add(new PlannedSection
                    {
                        FloorId = floor.Id,
                        FloorDisplayName = floorDisplayName,
                        Direction = dir,
                        SectionViewName = null, // assigned below, after merge
                        EdgeLengthMm = UnitUtils.ConvertFromInternalUnits(edge.LengthFeet, UnitTypeId.Millimeters),
                        EdgeCurve = edge.Curve,
                        EdgeMidpoint = edge.Midpoint,
                        InwardNormal = edge.InwardNormal,
                        FloorBoundingBox = bbox,
                        Status = PlannedSectionStatus.Ready,
                        IsIncluded = true,
                        FloorElement = floor
                    });
                }
            }

            int detectedCount = plan.Count(p => p.Status == PlannedSectionStatus.Ready);

            if (settings.MergeEnabled)
            {
                ApplyProximityMerge(plan, settings.MergeDistanceMm, log);
            }

            int suggestedCount = plan.Count(p => p.Status == PlannedSectionStatus.Ready);

            // Naming pass: only rows that survived the merge get a name, a Number slot,
            // and an entry in existingViewNames — so the sequence has no gaps from
            // rows that will never actually be created.
            int nextNumber = 1;
            foreach (PlannedSection row in plan.Where(p => p.Status == PlannedSectionStatus.Ready))
            {
                string viewName = SectionNamingService.BuildSectionViewName(
                    row.FloorElement, row.FloorDisplayName, row.Direction, settings, nextNumber, existingViewNames, out bool wasRenamed);
                nextNumber++;

                row.SectionViewName = viewName;

                if (wasRenamed)
                {
                    log.Add(new LogEntry(LogLevel.Warning, $"{row.FloorDisplayName}/{row.Direction}: name collision — renamed to {viewName}."));
                }
            }

            int noEdge = plan.Count(p => p.Status == PlannedSectionStatus.NoEdgeFound);
            int mergedOut = plan.Count(p => p.Status == PlannedSectionStatus.MergedOut);

            log.Add(new LogEntry(LogLevel.Success,
                $"Plan built: {detectedCount} detected, {suggestedCount} suggested after merge, {mergedOut} merged out, {noEdge} no edge found."));

            return new PlanBuildResult
            {
                Plan = plan,
                TotalFloorsCount = selectedFloors.Count,
                DetectedCount = detectedCount,
                SuggestedCount = suggestedCount
            };
        }

        /// <summary>
        /// Proximity-merge pass: walks all Ready rows in detection order (across ALL
        /// floors — global, not per-floor, per confirmed spec). For each row, if its
        /// EdgeMidpoint falls within mergeDistanceMm of an already-kept row's midpoint,
        /// the later row is marked MergedOut (first-found wins) rather than removed
        /// from the list, so it remains visible in the table for transparency.
        /// </summary>
        private static void ApplyProximityMerge(
            ObservableCollection<PlannedSection> plan,
            double mergeDistanceMm,
            ObservableCollection<LogEntry> log)
        {
            double mergeDistanceFeet = UnitUtils.ConvertToInternalUnits(mergeDistanceMm, UnitTypeId.Millimeters);

            var kept = new List<PlannedSection>();
            int mergedCount = 0;

            foreach (PlannedSection row in plan.Where(p => p.Status == PlannedSectionStatus.Ready))
            {
                PlannedSection nearKeeper = kept.FirstOrDefault(k =>
                    k.EdgeMidpoint.DistanceTo(row.EdgeMidpoint) <= mergeDistanceFeet);

                if (nearKeeper != null)
                {
                    row.Status = PlannedSectionStatus.MergedOut;
                    row.IsIncluded = false;
                    row.MergedIntoDescription = $"{nearKeeper.FloorDisplayName}/{nearKeeper.Direction}";
                    mergedCount++;

                    log.Add(new LogEntry(LogLevel.Warning,
                        $"{row.FloorDisplayName}/{row.Direction}: merged out — within {mergeDistanceMm:F0}mm of {nearKeeper.FloorDisplayName}/{nearKeeper.Direction}."));
                }
                else
                {
                    kept.Add(row);
                }
            }

            if (mergedCount > 0)
            {
                log.Add(new LogEntry(LogLevel.Info, $"Proximity merge: {mergedCount} section(s) merged out (threshold {mergeDistanceMm:F0}mm)."));
            }
        }
    }
}
