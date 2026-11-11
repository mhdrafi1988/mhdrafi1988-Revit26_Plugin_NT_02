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
    /// Creates ViewSection elements from the confirmed (checked) rows of the plan.
    /// One Transaction per floor (all directions for that floor committed together).
    /// Copied from FloorEdgeAroundSections_V004's SectionCreationService essentially
    /// unchanged — same BuildSectionBoundingBox axis pattern. The only functional
    /// difference is that EdgeMidpoint here is the matched cluster's representative
    /// point (projected onto the edge curve), not the full edge midpoint.
    /// </summary>
    public class SectionCreationService
    {
        public RunResult CreateSections(
            Document doc,
            IEnumerable<PlannedSection> rowsToProcess,
            FloorEdgeElementSectionsSettings settings,
            ViewFamilyType sectionViewFamilyType,
            ViewTemplateOption viewTemplate,
            ObservableCollection<LogEntry> log)
        {
            var result = new RunResult();

            double belowFloorFeet = UnitUtils.ConvertToInternalUnits(settings.BelowFloorMm, UnitTypeId.Millimeters);
            double aboveFloorFeet = UnitUtils.ConvertToInternalUnits(settings.AboveFloorMm, UnitTypeId.Millimeters);
            double farLengthFeet = UnitUtils.ConvertToInternalUnits(settings.FarClipMm, UnitTypeId.Millimeters);
            double marginOutwardFeet = UnitUtils.ConvertToInternalUnits(settings.MarginOutwardMm, UnitTypeId.Millimeters);
            double lengthInsideFloorFeet = UnitUtils.ConvertToInternalUnits(settings.LengthInsideFloorMm, UnitTypeId.Millimeters);

            var byFloor = rowsToProcess
                .Where(r => r.IsIncluded && r.Status == PlannedSectionStatus.Ready)
                .GroupBy(r => r.FloorId);

            foreach (var floorGroup in byFloor)
            {
                ToolCancellation.CheckCurrent();
                using (Transaction t = new Transaction(doc, $"Create Floor Edge Element Sections — Floor {floorGroup.Key.Value}"))
                {
                    t.Start();
                    int thisFloorCreated = 0;
                    try
                    {
                        foreach (PlannedSection row in floorGroup)
                        {
                            ToolCancellation.CheckCurrent();
                            log.Add(new LogEntry(LogLevel.Info,
                                $"Creating {row.SectionViewName} ({row.MatchedCategoryName}, {row.MatchedElementCount} element(s))..."));

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
                                $"Floor {floorGroup.Key.Value}: transaction did not commit (status: {status}). All sections for this floor rolled back."));

                            result.CreatedCount -= thisFloorCreated;
                            result.CreatedViewIds.RemoveRange(
                                result.CreatedViewIds.Count - thisFloorCreated, thisFloorCreated);
                            result.FailedCount += floorGroup.Count();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
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

            result.SkippedCount = rowsToProcess.Count(r => !r.IsIncluded || r.Status != PlannedSectionStatus.Ready);

            log.Add(new LogEntry(LogLevel.Success, $"Run complete — {result.SummaryLine}"));

            return result;
        }

        /// <summary>
        /// Builds the section's bounding box from fixed, explicit distances on all three
        /// axes, measured directly from the matched cluster's representative point
        /// (row.EdgeMidpoint — projected onto the edge curve, not the full edge midpoint).
        ///   BasisX (screen horizontal) = InwardNormal, perpendicular to the edge.
        ///   BasisY (vertical) = world up.
        ///   BasisZ (view/look direction, along the edge tangent) = far-clip depth.
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

            XYZ tangent = row.InwardNormal.CrossProduct(upDir).Normalize();
            XYZ viewDir = tangent;
            XYZ rightDir = row.InwardNormal;

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
