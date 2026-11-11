using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Models;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Services;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Engine
{
    /// <summary>
    /// Orchestrates the Linear-group (Wall/Beam) pipeline for one enabled
    /// ElementMapping, per spec Section 18:
    ///
    ///   Find Matching Linked Elements
    ///     -> Spatial Candidate Filter (Stage 1, same as Profile)
    ///     -> Obtain LocationCurve (centerline; V1 prefers this over full 3D boundary)
    ///     -> Transform Link -> Host
    ///     -> Project to Plan
    ///     -> Clip to View Boundary (single-curve trim, no loop-closing logic)
    ///     -> Create Detail Curve(s)
    ///     -> Apply Detail Line Style
    ///     -> Apply Color Override
    ///     -> Store Metadata
    ///
    /// Mirrors ProfileProcessingEngine's structure closely but works on a single
    /// centerline curve per element rather than outer/inner boundary loops.
    /// </summary>
    public class LinearProcessingEngine
    {
        private readonly SpatialFilterService _spatialFilter = new();
        private readonly LinearGeometryExtractionService _extraction = new();
        private readonly GeometryTransformService _transform = new();
        private readonly GeometryProjectionService _projection = new();
        private readonly GeometryClippingService _clipping = new();
        private readonly LineJoiningService _lineJoining = new();
        private readonly DetailLineCreationService _lineCreation = new();
        private readonly GraphicsOverrideService _graphicsOverride = new();
        private readonly MetadataService _metadata = new();
        private readonly DetailLineStyleService _lineStyleService = new();

        public MappingProcessingResult ProcessMapping(
            Document hostDoc,
            View activeView,
            RevitLinkInstance linkInstance,
            ElementMapping mapping,
            List<XYZ> processingBoundary,
            ProcessingScope scope,
            ComplexCurveSettings complexCurveSettings,
            Action<string, LogSeverity>? onLog = null)
        {
            var result = new MappingProcessingResult { MappingId = mapping.MappingId };
            void Log(string msg, LogSeverity sev = LogSeverity.Info) => onLog?.Invoke(msg, sev);

            Document? linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null)
            {
                Log($"Mapping '{mapping.CategoryName}/{mapping.TypeName}': link document not loaded, skipped.", LogSeverity.Warning);
                return result;
            }

            Transform linkToHost = linkInstance.GetTotalTransform();

            List<Element> matchingElements;
            try
            {
                BuiltInCategory bic = mapping.CategoryName == "Wall"
                    ? BuiltInCategory.OST_Walls
                    : BuiltInCategory.OST_StructuralFraming;

                matchingElements = new FilteredElementCollector(linkedDoc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .Where(e => e.GetTypeId().Value == mapping.TypeId)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log($"Mapping '{mapping.TypeName}': failed to query linked elements: {ex.Message}", LogSeverity.Error);
                return result;
            }

            result.ElementsFound = matchingElements.Count;
            Log($"{mapping.CategoryName} / {mapping.TypeName}: {matchingElements.Count} candidate(s) found in '{mapping.LinkDisplayName}'.");

            List<Element> spatialCandidates = _spatialFilter.FilterCandidates(
                matchingElements, linkToHost, processingBoundary, linkedDoc);

            Log($"{mapping.TypeName}: {spatialCandidates.Count} candidate(s) survived spatial pre-filter.");

            GraphicsStyle? lineStyle = _lineStyleService.FindByName(hostDoc, mapping.DetailLineStyleName);
            if (lineStyle == null && !string.IsNullOrWhiteSpace(mapping.DetailLineStyleName))
                Log($"Detail Line Style '{mapping.DetailLineStyleName}' not found in host project — lines created with default style.", LogSeverity.Warning);

            double planZ = activeView.GenLevel?.Elevation ?? activeView.Origin.Z;

            foreach (var elem in spatialCandidates)
            {
                ToolCancellation.CheckCurrent();
                try
                {
                    ProcessSingleElement(
                        hostDoc, activeView, elem, mapping, linkToHost, processingBoundary,
                        scope, complexCurveSettings, lineStyle, planZ, result, Log);
                }
                catch (Exception ex)
                {
                    result.ElementsSkipped++;
                    result.Errors.Add(new ProcessingError
                    {
                        ElementId = elem.Id.Value,
                        CategoryName = mapping.CategoryName,
                        Reason = ex.Message
                    });
                    Log($"Element {elem.Id.Value} ({mapping.TypeName}): unhandled error — {ex.Message}", LogSeverity.Error);
                }
            }

            return result;
        }

        private void ProcessSingleElement(
            Document hostDoc, View activeView, Element elem, ElementMapping mapping,
            Transform linkToHost, List<XYZ> processingBoundary, ProcessingScope scope,
            ComplexCurveSettings complexCurveSettings, GraphicsStyle? lineStyle,
            double planZ, MappingProcessingResult result, Action<string, LogSeverity> log)
        {
            Curve? centerline = _extraction.ExtractCenterline(elem, complexCurveSettings,
                (msg, id) =>
                {
                    result.ElementsSkipped++;
                    result.Errors.Add(new ProcessingError { ElementId = elem.Id.Value, CategoryName = mapping.CategoryName, Reason = msg });
                    log($"Element {elem.Id.Value}: {msg}", LogSeverity.Warning);
                });

            if (centerline == null) return;

            log($"Element {elem.Id.Value}: centerline extracted from LocationCurve ({centerline.GetType().Name}, length {centerline.Length:F2} ft).",
                LogSeverity.Debug);

            Curve hostCurve = _transform.TransformCurves(new List<Curve> { centerline }, linkToHost)[0];
            Curve planCurve = _projection.ProjectToPlan(new List<Curve> { hostCurve }, planZ)[0];

            LinearClipResult clip = _clipping.ClipLinearCurve(planCurve, processingBoundary, scope.TrimToBoundary);

            foreach (var w in clip.Warnings)
                log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning);

            log($"Element {elem.Id.Value}: centerline clipped — fullyInside={clip.FullyInside}, fullyOutside={clip.FullyOutside}, {clip.Segments.Count} resulting segment(s).",
                LogSeverity.Debug);

            if (clip.FullyOutside)
            {
                // Not an error — simply outside processing scope. Excluded from both
                // Processed and Skipped tallies to avoid misrepresenting
                // geometrically-irrelevant elements as failures.
                return;
            }

            if (clip.Segments.Count == 0)
            {
                result.ElementsSkipped++;
                return;
            }

            bool anyCleanup = scope.RemoveEngulfedOnly || scope.MergePartialOverlaps || scope.JoinCollinearLines;
            List<Curve> segmentsToCreate = anyCleanup
                ? _lineJoining.ProcessLines(clip.Segments, MmToFeet(scope.LineJoinToleranceMm),
                    scope.RemoveEngulfedOnly, scope.MergePartialOverlaps, scope.JoinCollinearLines)
                : clip.Segments;

            if (anyCleanup && segmentsToCreate.Count != clip.Segments.Count)
                log($"Element {elem.Id.Value}: overlap/collinear cleanup reduced {clip.Segments.Count} segment(s) to {segmentsToCreate.Count}.", LogSeverity.Debug);

            List<DetailCurve> createdCurves = _lineCreation.CreateDetailCurves(
                hostDoc, activeView, segmentsToCreate,
                w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

            if (createdCurves.Count == 0)
            {
                result.ElementsSkipped++;
                return;
            }

            log($"Element {elem.Id.Value}: {createdCurves.Count} Detail Line(s) created.", LogSeverity.Debug);

            _lineCreation.ApplyLineStyle(createdCurves, lineStyle,
                w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

            foreach (var dc in createdCurves)
            {
                _graphicsOverride.ApplyColorOverride(hostDoc, activeView, dc.Id, mapping.ColorName,
                    w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

                _metadata.WriteMetadata(
                    dc,
                    mapping.LinkInstanceId,
                    mapping.LinkDisplayName,
                    mapping.CategoryName,
                    mapping.FamilyName,
                    mapping.TypeName,
                    elem.Id.Value,
                    mapping.Representation.ToString(),
                    mapping.MappingId);
            }

            result.DetailLinesCreated += createdCurves.Count;
            result.CreatedGroups.Add(new CreatedElementGroup
            {
                ElementIds = createdCurves.Select(c => c.Id.Value).ToList(),
                IsPointMarker = false
            });
            result.ElementsProcessed++;
        }

        private static double MmToFeet(double mm) => mm / 304.8;
    }
}
