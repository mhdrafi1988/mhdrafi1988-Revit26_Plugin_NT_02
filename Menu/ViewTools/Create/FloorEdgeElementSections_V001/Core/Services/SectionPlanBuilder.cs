using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Builds the Planned Sections preview list: buckets each selected floor's edges
    /// (FloorEdgeBucketingEngine, reused verbatim), builds the candidate list from
    /// checked tree leaves across selected/checked links (transformed to host space
    /// via linkInstance.GetTotalTransform()), bbox-pre-filters candidates per floor,
    /// runs ElementEdgeMatchingEngine per floor, then names the surviving Ready rows.
    /// </summary>
    public class SectionPlanBuilder
    {
        public class PlanBuildResult
        {
            public ObservableCollection<PlannedSection> Plan { get; set; }
            public int TotalFloorsCount { get; set; }
            public int DetectedElementCount { get; set; }
            public int SuggestedSectionCount { get; set; }
        }

        public PlanBuildResult BuildPlan(
            Document hostDoc,
            IList<Floor> selectedFloors,
            IEnumerable<LinkTreeNode> elementTree,
            double viewRotationRadians,
            FloorEdgeElementSectionsSettings settings,
            ObservableCollection<LogEntry> log)
        {
            var plan = new ObservableCollection<PlannedSection>();

            log.Add(new LogEntry(LogLevel.Info, $"Floors selected: {selectedFloors.Count}."));

            if (selectedFloors.Count == 0)
            {
                log.Add(new LogEntry(LogLevel.Warning, "No floors picked — nothing to plan. Use \"Pick Floors\" above."));
                return new PlanBuildResult { Plan = plan, TotalFloorsCount = 0, DetectedElementCount = 0, SuggestedSectionCount = 0 };
            }

            List<LinkedElementCandidate> allCandidates = BuildCandidateList(hostDoc, elementTree, log);
            log.Add(new LogEntry(LogLevel.Info, $"{allCandidates.Count} candidate element(s) from checked linked-model types."));

            if (allCandidates.Count == 0)
            {
                log.Add(new LogEntry(LogLevel.Warning, "No linked-element types checked — nothing to plan."));
                return new PlanBuildResult { Plan = plan, TotalFloorsCount = selectedFloors.Count, DetectedElementCount = 0, SuggestedSectionCount = 0 };
            }

            double edgeToleranceFeet = UnitUtils.ConvertToInternalUnits(settings.EdgeProximityToleranceMm, UnitTypeId.Millimeters);
            double dedupToleranceFeet = UnitUtils.ConvertToInternalUnits(settings.SameSideDedupToleranceMm, UnitTypeId.Millimeters);

            HashSet<string> existingViewNames = SectionNamingService.GetExistingViewNames(hostDoc);

            foreach (Floor floor in selectedFloors)
            {
                ToolCancellation.CheckCurrent();
                BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
                if (bbox == null)
                {
                    log.Add(new LogEntry(LogLevel.Warning, $"Floor {floor.Id.Value}: no bounding box available — skipped entirely."));
                    continue;
                }

                string floorDisplayName = SectionNamingService.GetFloorDisplayName(floor);

                var bucketed = FloorEdgeBucketingEngine.BucketEdges(floor, bbox, viewRotationRadians, log);
                if (bucketed.Count == 0)
                {
                    log.Add(new LogEntry(LogLevel.Warning, $"Floor {floorDisplayName}: no edges bucketed — skipped."));
                    continue;
                }

                // Cheap bbox pre-filter before the per-floor matching pass.
                XYZ expand = new XYZ(edgeToleranceFeet, edgeToleranceFeet, edgeToleranceFeet);
                XYZ min = bbox.Min - expand;
                XYZ max = bbox.Max + expand;

                var floorCandidates = allCandidates.Where(c =>
                    c.HostSpacePosition.X >= min.X && c.HostSpacePosition.X <= max.X &&
                    c.HostSpacePosition.Y >= min.Y && c.HostSpacePosition.Y <= max.Y &&
                    c.HostSpacePosition.Z >= min.Z && c.HostSpacePosition.Z <= max.Z)
                    .ToList();

                if (floorCandidates.Count == 0)
                    continue;

                XYZ floorCenter = (bbox.Min + bbox.Max) * 0.5;

                var matches = ElementEdgeMatchingEngine.MatchAndCluster(
                    bucketed, floorCandidates, edgeToleranceFeet, dedupToleranceFeet, floorCenter, log);

                foreach (var match in matches)
                {
                    ToolCancellation.CheckCurrent();
                    FloorEdgeBucketingEngine.BucketedEdge edge = bucketed[match.Direction];

                    plan.Add(new PlannedSection
                    {
                        FloorId = floor.Id,
                        FloorElement = floor,
                        FloorDisplayName = floorDisplayName,
                        Direction = match.Direction,
                        SectionViewName = null, // assigned below, after the naming pass
                        MatchedCategoryName = match.CategoryName,
                        MatchedElementRefs = match.MemberRefs,
                        MatchedElementCount = match.Count,
                        EdgeCurve = edge.Curve,
                        EdgeMidpoint = match.RepresentativePoint,
                        InwardNormal = edge.InwardNormal,
                        FloorBoundingBox = bbox,
                        Status = match.ClusterCapped ? PlannedSectionStatus.ClusterCapped : PlannedSectionStatus.Ready,
                        IsIncluded = !match.ClusterCapped,
                        MergedIntoDescription = match.ClusterCapped
                            ? $"exceeds 2-per-side cap for {match.CategoryName} on {match.Direction}"
                            : null
                    });
                }
            }

            int detectedElementCount = allCandidates.Count;
            int suggestedSectionCount = plan.Count(p => p.Status == PlannedSectionStatus.Ready);

            // Naming pass: only Ready rows get a name and a Number slot.
            int nextNumber = 1;
            foreach (PlannedSection row in plan.Where(p => p.Status == PlannedSectionStatus.Ready))
            {
                string viewName = SectionNamingService.BuildSectionViewName(
                    row.FloorElement, row.FloorDisplayName, row.Direction, row.MatchedCategoryName,
                    settings, nextNumber, existingViewNames, out bool wasRenamed);
                nextNumber++;

                row.SectionViewName = viewName;

                if (wasRenamed)
                {
                    log.Add(new LogEntry(LogLevel.Warning, $"{row.FloorDisplayName}/{row.Direction}/{row.MatchedCategoryName}: name collision — renamed to {viewName}."));
                }
            }

            foreach (PlannedSection row in plan.Where(p => p.Status == PlannedSectionStatus.ClusterCapped))
                row.SectionViewName = "—";

            int capped = plan.Count(p => p.Status == PlannedSectionStatus.ClusterCapped);

            log.Add(new LogEntry(LogLevel.Success,
                $"Plan built: {plan.Count} row(s) — {suggestedSectionCount} ready, {capped} cluster-capped."));

            return new PlanBuildResult
            {
                Plan = plan,
                TotalFloorsCount = selectedFloors.Count,
                DetectedElementCount = detectedElementCount,
                SuggestedSectionCount = suggestedSectionCount
            };
        }

        /// <summary>
        /// Builds the candidate list once from checked TypeTreeItem leaves across all
        /// selected/checked links — for each matching linked element, gets its
        /// representative point (LocationPoint/LocationCurve midpoint, falling back to
        /// bbox center) in link-local space, then transforms it to host space via
        /// linkInstance.GetTotalTransform().OfPoint(...).
        /// </summary>
        private static List<LinkedElementCandidate> BuildCandidateList(
            Document hostDoc,
            IEnumerable<LinkTreeNode> elementTree,
            IList<LogEntry> log)
        {
            var candidates = new List<LinkedElementCandidate>();

            foreach (LinkTreeNode node in elementTree)
            {
                bool hasCheckedType = node.Categories
                    .SelectMany(c => c.Families)
                    .SelectMany(f => f.Types)
                    .Any(t => t.IsChecked);
                if (!hasCheckedType) continue;

                RevitLinkInstance linkInstance = hostDoc.GetElement(new ElementId(node.LinkInstanceId)) as RevitLinkInstance;
                if (linkInstance == null) continue;

                Document linkedDoc;
                try
                {
                    linkedDoc = linkInstance.GetLinkDocument();
                }
                catch
                {
                    continue;
                }
                if (linkedDoc == null) continue;

                Transform xform = linkInstance.GetTotalTransform();

                foreach (CategoryTreeItem cat in node.Categories)
                {
                    foreach (FamilyTreeItem fam in cat.Families)
                    {
                        foreach (TypeTreeItem t in fam.Types)
                        {
                            if (!t.IsChecked) continue;

                            ElementId typeId = new ElementId(t.TypeId);
                            List<Element> elements;
                            try
                            {
                                elements = new FilteredElementCollector(linkedDoc)
                                    .WhereElementIsNotElementType()
                                    .Where(e => e.GetTypeId() == typeId)
                                    .ToList();
                            }
                            catch (OperationCanceledException)
                            {
                                throw; // user cancel: never swallowed as a failure
                            }
                            catch (Exception ex)
                            {
                                log.Add(new LogEntry(LogLevel.Warning, $"{node.LinkDisplayName}/{cat.CategoryName}/{t.TypeName}: failed to collect instances — {ex.Message}"));
                                continue;
                            }

                            foreach (Element elem in elements)
                            {
                                XYZ localPos = GetElementRepresentativePoint(elem);
                                if (localPos == null) continue;

                                XYZ hostPos = xform.OfPoint(localPos);
                                candidates.Add(new LinkedElementCandidate
                                {
                                    ElementId = elem.Id.Value,
                                    LinkInstanceId = node.LinkInstanceId,
                                    CategoryName = cat.CategoryName,
                                    HostSpacePosition = hostPos
                                });
                            }
                        }
                    }
                }
            }

            return candidates;
        }

        private static XYZ GetElementRepresentativePoint(Element e)
        {
            if (e.Location is LocationPoint lp)
                return lp.Point;

            if (e.Location is LocationCurve lc)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;

            BoundingBoxXYZ bbox = e.get_BoundingBox(null);
            return bbox != null ? (bbox.Min + bbox.Max) * 0.5 : null;
        }
    }
}
