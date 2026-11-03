using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Creates ViewSection elements from the confirmed (checked) rows of the plan.
    /// One Transaction per floor (all directions for that floor committed together),
    /// so a partial failure on one floor does not leave orphan sections for that
    /// floor, while a failure on one floor does not abort other floors' runs.
    /// </summary>
    public class SectionCreationService
    {
        public RunResult CreateSections(
            Document doc,
            IEnumerable<PlannedSection> rowsToProcess,
            FloorEdgeSectionsSettings settings,
            ViewFamilyType sectionViewFamilyType,
            ViewTemplateOption viewTemplate,
            ObservableCollection<LogEntry> log)
        {
            var result = new RunResult();

            double belowFloorFeet = UnitUtils.ConvertToInternalUnits(settings.BelowFloorMm, UnitTypeId.Millimeters);
            double aboveFloorFeet = UnitUtils.ConvertToInternalUnits(settings.AboveFloorMm, UnitTypeId.Millimeters);
            double farLengthFeet = UnitUtils.ConvertToInternalUnits(settings.SectionFarLengthMm, UnitTypeId.Millimeters);
            double marginOutwardFeet = UnitUtils.ConvertToInternalUnits(settings.MarginOutwardMm, UnitTypeId.Millimeters);
            double lengthInsideFloorFeet = UnitUtils.ConvertToInternalUnits(settings.LengthInsideFloorMm, UnitTypeId.Millimeters);

            var byFloor = rowsToProcess
                .Where(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready)
                .GroupBy(r => r.FloorId);

            foreach (var floorGroup in byFloor)
            {
                using (Transaction t = new Transaction(doc, $"Create Floor Edge Sections — Floor {floorGroup.Key.Value}"))
                {
                    t.Start();
                    int thisFloorCreated = 0; // per-floor, not the cumulative CreatedViewIds.Count —
                                              // fixes over-subtraction on rollback when an earlier
                                              // floor in the same Run already succeeded.
                    try
                    {
                        foreach (PlannedSection row in floorGroup)
                        {
                            log.Add(new LogEntry(LogLevel.Info,
                                $"Creating {row.SectionViewName} (edge length {row.EdgeLengthMm:F0} mm)..."));

                            try
                            {
                                BoundingBoxXYZ sectionBox = BuildSectionBoundingBox(
                                    row, belowFloorFeet, aboveFloorFeet, farLengthFeet, marginOutwardFeet, lengthInsideFloorFeet);

                                ViewSection view = ViewSection.CreateSection(doc, sectionViewFamilyType.Id, sectionBox);
                                view.Name = row.SectionViewName;

                                if (viewTemplate != null && viewTemplate.TemplateId != ElementId.InvalidElementId)
                                {
                                    view.ViewTemplateId = viewTemplate.TemplateId;
                                }

                                result.CreatedViewIds.Add(view.Id);
                                result.CreatedCount++;
                                thisFloorCreated++;

                                log.Add(new LogEntry(LogLevel.Success, $"{row.SectionViewName} created (View Id {view.Id.Value})."));
                            }
                            catch (Exception exRow)
                            {
                                result.FailedCount++;
                                log.Add(new LogEntry(LogLevel.Warning, $"{row.SectionViewName}: creation failed — {exRow.Message}. Skipped."));
                            }
                        }

                        TransactionStatus status = t.Commit();
                        if (status != TransactionStatus.Committed)
                        {
                            log.Add(new LogEntry(LogLevel.Error,
                                $"Floor {floorGroup.Key.Value}: transaction did not commit (status: {status}). All sections for this floor rolled back."));

                            // Reconcile counters using ONLY this floor's created count — subtracting
                            // the cumulative CreatedViewIds.Count here would incorrectly wipe out
                            // successes from earlier floors already committed in this same Run.
                            result.CreatedCount -= thisFloorCreated;
                            result.CreatedViewIds.RemoveRange(
                                result.CreatedViewIds.Count - thisFloorCreated, thisFloorCreated);
                            result.FailedCount += floorGroup.Count();
                        }
                    }
                    catch (Exception exFloor)
                    {
                        if (t.GetStatus() == TransactionStatus.Started)
                            t.RollBack();

                        result.CreatedCount -= thisFloorCreated;
                        if (thisFloorCreated > 0)
                        {
                            result.CreatedViewIds.RemoveRange(
                                result.CreatedViewIds.Count - thisFloorCreated, thisFloorCreated);
                        }

                        log.Add(new LogEntry(LogLevel.Error,
                            $"Floor {floorGroup.Key.Value}: unexpected error, transaction rolled back — {exFloor.Message}"));
                        result.FailedCount += floorGroup.Count();
                    }
                }
            }

            // Rows skipped before Run (NoEdgeFound / MergedOut / unchecked) count toward SkippedCount.
            result.SkippedCount = rowsToProcess.Count(r => !r.IsIncluded || r.Status != PlannedSectionStatus.Ready);

            log.Add(new LogEntry(LogLevel.Success, $"Run complete — {result.SummaryLine}"));

            return result;
        }

        /// <summary>
        /// Builds the section's bounding box from fixed, explicit distances on all three axes —
        /// no automatic wall search (V004 drops NearbyWallFinder entirely).
        ///
        /// Axis layout, all measured directly from the floor edge (origin = EdgeMidpoint, no
        /// pull-back offset):
        ///   BasisX (screen horizontal) = InwardNormal — perpendicular to the edge.
        ///     Negative X (MarginOutwardMm) = OUTWARD, away from the floor (eave/fascia side).
        ///     Positive X (LengthInsideFloorMm) = INWARD, into the floor toward the wall/structure.
        ///   BasisY (vertical) = world up.
        ///     Negative Y = below the floor edge's own elevation, reaching down to -BelowFloorMm.
        ///     Positive Y = above the floor edge's own elevation, reaching up to +AboveFloorMm.
        ///   BasisZ (view/look direction, along the edge tangent) = far-clip depth, symmetric
        ///     around the origin, sized by SectionFarLengthMm — how much of the edge's length
        ///     is captured in the section.
        ///
        /// V005: same as V004, plus CreateSections_V011's orientation rule — the view
        /// direction is never allowed to look down or pure-left in plan, so sections read
        /// consistently (matches the flip rule in SectionOrientationService).
        /// </summary>
        private static BoundingBoxXYZ BuildSectionBoundingBox(
            PlannedSection row,
            double belowFloorFeet,
            double aboveFloorFeet,
            double farLengthFeet,
            double marginOutwardFeet,
            double lengthInsideFloorFeet)
        {
            XYZ upDir = XYZ.BasisZ;

            // Along-edge direction (view/look direction), derived from InwardNormal crossed with up.
            XYZ tangent = row.InwardNormal.CrossProduct(upDir).Normalize();
            XYZ viewDir = tangent;

            // Perpendicular to edge, into the floor — the outward/inward crop axis.
            XYZ rightDir = row.InwardNormal;

            // Flip ONLY the viewing direction when it points to bad quadrants in plan:
            // Down, Down-Left, Down-Right, or pure Left (same rule as
            // CreateSections_V011's SectionOrientationService.Calculate).
            bool isLookingDown = viewDir.Y < -1e-9;
            bool isPureLeft = Math.Abs(viewDir.Y) < 1e-9 && viewDir.X < -1e-9;

            if (isLookingDown || isPureLeft)
            {
                rightDir = rightDir.Negate();
                viewDir = viewDir.Negate();
            }

            // Origin sits directly on the floor edge — outward/inward and below/above distances
            // are all measured from here, with no separate pull-back offset.
            XYZ origin = row.EdgeMidpoint;

            Transform t = Transform.Identity;
            t.Origin = origin;
            t.BasisX = rightDir;
            t.BasisY = upDir;
            t.BasisZ = viewDir;

            double halfFarLength = farLengthFeet / 2.0;

            var box = new BoundingBoxXYZ
            {
                Transform = t,
                Min = new XYZ(-marginOutwardFeet, -belowFloorFeet, -halfFarLength),
                Max = new XYZ(lengthInsideFloorFeet, aboveFloorFeet, halfFarLength)
            };

            return box;
        }
    }

    /// <summary>Thin wrapper describing the chosen View Template (or none).</summary>
    public class ViewTemplateOption
    {
        public string Name { get; set; }
        public ElementId TemplateId { get; set; } = ElementId.InvalidElementId;
    }
}
