using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V007
{
    /// <summary>
    /// Creates ViewSection elements from the confirmed (checked) rows of the plan.
    /// One Transaction per roof (all directions for that roof committed together),
    /// so a partial failure on one roof does not leave orphan sections for that
    /// roof, while a failure on one roof does not abort other roofs' runs.
    /// </summary>
    public class SectionCreationService
    {
        public RunResult CreateSections(
            Document doc,
            IEnumerable<PlannedSection> rowsToProcess,
            RoofEdgeSectionsSettings settings,
            ViewFamilyType sectionViewFamilyType,
            ViewTemplateOption viewTemplate,
            ObservableCollection<LogEntry> log)
        {
            var result = new RunResult();

            double belowRoofFeet = UnitUtils.ConvertToInternalUnits(settings.BelowRoofMm, UnitTypeId.Millimeters);
            double aboveRoofFeet = UnitUtils.ConvertToInternalUnits(settings.AboveRoofMm, UnitTypeId.Millimeters);
            double farLengthFeet = UnitUtils.ConvertToInternalUnits(settings.SectionFarLengthMm, UnitTypeId.Millimeters);
            double marginOutwardFeet = UnitUtils.ConvertToInternalUnits(settings.MarginOutwardMm, UnitTypeId.Millimeters);
            double lengthInsideRoofFeet = UnitUtils.ConvertToInternalUnits(settings.LengthInsideRoofMm, UnitTypeId.Millimeters);

            var byRoof = rowsToProcess
                .Where(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready)
                .GroupBy(r => r.RoofId);

            foreach (var roofGroup in byRoof)
            {
                ToolCancellation.CheckCurrent();
                using (Transaction t = new Transaction(doc, $"Create Roof Edge Sections — Roof {roofGroup.Key.Value}"))
                {
                    t.Start();
                    int thisRoofCreated = 0; // per-roof, not the cumulative CreatedViewIds.Count —
                                              // fixes over-subtraction on rollback when an earlier
                                              // roof in the same Run already succeeded.
                    try
                    {
                        foreach (PlannedSection row in roofGroup)
                        {
                            ToolCancellation.CheckCurrent();
                            log.Add(new LogEntry(LogLevel.Info,
                                $"Creating {row.SectionViewName} (edge length {row.EdgeLengthMm:F0} mm)..."));

                            try
                            {
                                BoundingBoxXYZ sectionBox = BuildSectionBoundingBox(
                                    row, belowRoofFeet, aboveRoofFeet, farLengthFeet, marginOutwardFeet, lengthInsideRoofFeet);

                                ViewSection view = ViewSection.CreateSection(doc, sectionViewFamilyType.Id, sectionBox);
                                view.Name = row.SectionViewName;

                                if (viewTemplate != null && viewTemplate.TemplateId != ElementId.InvalidElementId)
                                {
                                    view.ViewTemplateId = viewTemplate.TemplateId;
                                }

                                result.CreatedViewIds.Add(view.Id);
                                result.CreatedCount++;
                                thisRoofCreated++;

                                log.Add(new LogEntry(LogLevel.Success, $"{row.SectionViewName} created (View Id {view.Id.Value})."));
                            }
                            catch (OperationCanceledException)
                            {
                                throw; // user cancel: never swallowed as a failure
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
                                $"Roof {roofGroup.Key.Value}: transaction did not commit (status: {status}). All sections for this roof rolled back."));

                            // Reconcile counters using ONLY this roof's created count — subtracting
                            // the cumulative CreatedViewIds.Count here would incorrectly wipe out
                            // successes from earlier roofs already committed in this same Run.
                            result.CreatedCount -= thisRoofCreated;
                            result.CreatedViewIds.RemoveRange(
                                result.CreatedViewIds.Count - thisRoofCreated, thisRoofCreated);
                            result.FailedCount += roofGroup.Count();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception exRoof)
                    {
                        if (t.GetStatus() == TransactionStatus.Started)
                            t.RollBack();

                        result.CreatedCount -= thisRoofCreated;
                        if (thisRoofCreated > 0)
                        {
                            result.CreatedViewIds.RemoveRange(
                                result.CreatedViewIds.Count - thisRoofCreated, thisRoofCreated);
                        }

                        log.Add(new LogEntry(LogLevel.Error,
                            $"Roof {roofGroup.Key.Value}: unexpected error, transaction rolled back — {exRoof.Message}"));
                        result.FailedCount += roofGroup.Count();
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
        /// Axis layout, all measured directly from the roof edge (origin = EdgeMidpoint, no
        /// pull-back offset):
        ///   BasisX (screen horizontal) = InwardNormal — perpendicular to the edge.
        ///     Negative X (MarginOutwardMm) = OUTWARD, away from the roof (eave/fascia side).
        ///     Positive X (LengthInsideRoofMm) = INWARD, into the roof toward the wall/structure.
        ///   BasisY (vertical) = world up.
        ///     Negative Y = below the roof edge's own elevation, reaching down to -BelowRoofMm.
        ///     Positive Y = above the roof edge's own elevation, reaching up to +AboveRoofMm.
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
            double belowRoofFeet,
            double aboveRoofFeet,
            double farLengthFeet,
            double marginOutwardFeet,
            double lengthInsideRoofFeet)
        {
            XYZ upDir = XYZ.BasisZ;

            // Along-edge direction (view/look direction), derived from InwardNormal crossed with up.
            XYZ tangent = row.InwardNormal.CrossProduct(upDir).Normalize();
            XYZ viewDir = tangent;

            // Perpendicular to edge, into the roof — the outward/inward crop axis.
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

            // Origin sits directly on the roof edge — outward/inward and below/above distances
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
                Min = new XYZ(-marginOutwardFeet, -belowRoofFeet, -halfFarLength),
                Max = new XYZ(lengthInsideRoofFeet, aboveRoofFeet, halfFarLength)
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
