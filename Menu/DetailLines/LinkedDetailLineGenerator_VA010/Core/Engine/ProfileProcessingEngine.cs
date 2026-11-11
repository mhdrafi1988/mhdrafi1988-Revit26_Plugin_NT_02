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
    /// Orchestrates the full Profile-group (Floor/Roof) pipeline for one enabled
    /// ElementMapping, per spec Section 16/17/24:
    ///
    ///   Find Matching Linked Elements
    ///     -> Spatial Candidate Filter (Stage 1)
    ///     -> Extract Geometry (outer + inner boundary loops, exact curve types)
    ///     -> Transform Link -> Host
    ///     -> Project to Plan (flatten to view's Z)
    ///     -> Clip to View Boundary (Stage 2: trim / close-loop / cap per ProcessingScope)
    ///     -> Create Detail Curves
    ///     -> Apply Detail Line Style
    ///     -> Apply Color Override
    ///     -> Store Metadata
    ///
    /// Must be called from within an active host-document Transaction (this class
    /// does not open/commit its own transaction -- see spec Section 25 and the
    /// ExternalEventHandler that wraps it).
    /// </summary>
    public class ProfileProcessingEngine
    {
        private readonly SpatialFilterService _spatialFilter = new();
        private readonly GeometryExtractionService _extraction = new();
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

            // ── Find matching linked elements (Category + Type filter) ──
            List<Element> matchingElements;
            try
            {
                BuiltInCategory bic = mapping.CategoryName == "Roof" ? BuiltInCategory.OST_Roofs : BuiltInCategory.OST_Floors;
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

            // ── Stage 1: spatial candidate filter ──
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
                        hostDoc, activeView, linkInstance, linkedDoc, elem, mapping,
                        linkToHost, processingBoundary, scope, complexCurveSettings,
                        lineStyle, planZ, result, Log);
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
            Document hostDoc, View activeView, RevitLinkInstance linkInstance, Document linkedDoc,
            Element elem, ElementMapping mapping, Transform linkToHost, List<XYZ> processingBoundary,
            ProcessingScope scope, ComplexCurveSettings complexCurveSettings, GraphicsStyle? lineStyle,
            double planZ, MappingProcessingResult result, Action<string, LogSeverity> log)
        {
            Action<string, ElementId?> onExtractWarning = (msg, id) =>
            {
                result.ElementsSkipped++;
                result.Errors.Add(new ProcessingError { ElementId = elem.Id.Value, CategoryName = mapping.CategoryName, Reason = msg });
                log($"Element {elem.Id.Value}: {msg}", LogSeverity.Warning);
            };

            var profile = mapping.ExtractionMethod == ProfileExtractionMethod.ProfileBased
                ? _extraction.ExtractProfileFromSketch(elem, complexCurveSettings, onExtractWarning,
                    (msg, id) => log($"Element {elem.Id.Value}: {msg}", LogSeverity.Debug))
                : _extraction.ExtractProfile(elem, complexCurveSettings, onExtractWarning);

            if (profile == null) return;

            string methodDetail = profile.ExtractionMethodUsed.StartsWith("ProfileBased")
                ? "sketch/profile curves"
                : $"top face (area {profile.SourceFaceArea:F1} sq ft, normal Z {profile.SourceFaceNormalZ:F3}{(profile.SourceFaceNormalZ < 0.999 ? " — sloped" : "")})";
            log($"Element {elem.Id.Value}: profile extracted via {profile.ExtractionMethodUsed} — {methodDetail} — {profile.OuterLoops.Count} outer loop(s), {profile.InnerLoops.Count} inner loop(s).",
                LogSeverity.Debug);

            int loopsCreated = 0;

            foreach (var loop in profile.OuterLoops)
                loopsCreated += ProcessLoop(
                    hostDoc, activeView, elem, mapping, linkToHost, processingBoundary, scope,
                    scope.OuterLoopClosing, "outer", loop, planZ, lineStyle, result, log);

            foreach (var loop in profile.InnerLoops)
                loopsCreated += ProcessLoop(
                    hostDoc, activeView, elem, mapping, linkToHost, processingBoundary, scope,
                    scope.InnerLoopClosing, "inner", loop, planZ, lineStyle, result, log);

            if (loopsCreated > 0)
                result.ElementsProcessed++;
            else
                result.ElementsSkipped++;
        }

        /// <summary>Transforms/projects/clips/joins/creates Detail Lines for one
        /// boundary loop (outer or inner), using the LoopClosingSettings appropriate
        /// to that loop kind. Returns 1 if at least one Detail Line was created for
        /// this loop, 0 otherwise (mirrors the old inline loopsCreated++ semantics).</summary>
        private int ProcessLoop(
            Document hostDoc, View activeView, Element elem, ElementMapping mapping,
            Transform linkToHost, List<XYZ> processingBoundary, ProcessingScope scope,
            LoopClosingSettings loopClosing, string loopKind, List<Curve> loop, double planZ,
            GraphicsStyle? lineStyle, MappingProcessingResult result, Action<string, LogSeverity> log)
        {
            List<Curve> hostCurves = _transform.TransformCurves(loop, linkToHost);
            List<Curve> planCurves = _projection.ProjectToPlan(hostCurves, planZ);

            ClipResult clip = _clipping.ClipLoop(planCurves, processingBoundary, scope.TrimToBoundary, loopClosing,
                w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

            foreach (var w in clip.Warnings)
                log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning);

            log($"Element {elem.Id.Value}: {loopKind} loop of {loop.Count} edge(s) clipped — fullyInside={clip.FullyInside}, fullyOutside={clip.FullyOutside}, {clip.Chains.Count} resulting chain(s).",
                LogSeverity.Debug);

            if (clip.FullyOutside)
                return 0; // not an error -- simply outside processing scope

            int created = 0;
            bool anyCleanup = scope.RemoveEngulfedOnly || scope.MergePartialOverlaps || scope.JoinCollinearLines;

            foreach (var chain in clip.Chains)
            {
                List<Curve> curvesToCreate = anyCleanup
                    ? _lineJoining.ProcessLines(chain, MmToFeet(scope.LineJoinToleranceMm),
                        scope.RemoveEngulfedOnly, scope.MergePartialOverlaps, scope.JoinCollinearLines)
                    : chain;

                if (anyCleanup && curvesToCreate.Count != chain.Count)
                    log($"Element {elem.Id.Value}: overlap/collinear cleanup reduced {chain.Count} edge(s) to {curvesToCreate.Count}.", LogSeverity.Debug);

                List<DetailCurve> createdCurves = _lineCreation.CreateDetailCurves(
                    hostDoc, activeView, curvesToCreate,
                    w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

                if (createdCurves.Count == 0) continue;

                log($"Element {elem.Id.Value}: {createdCurves.Count} Detail Line(s) created for this {loopKind} loop.", LogSeverity.Debug);

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
                created += createdCurves.Count;

                // OUTPUT GENERATION (Section 5): a closed OUTER loop optionally also
                // gets a Filled Region covering its area, in addition to its Detail
                // Lines. Inner loops (openings) are never filled — that would fill
                // the hole, not the material.
                if (scope.CreateFilledRegions && loopKind == "outer"
                    && IsChainClosed(curvesToCreate, hostDoc.Application.ShortCurveTolerance))
                {
                    TryCreateFilledRegion(hostDoc, activeView, elem, mapping, curvesToCreate, result, log);
                }
            }

            return created > 0 ? 1 : 0;
        }

        /// <summary>A chain is closed when its last curve's end point meets its first
        /// curve's start point within Revit's own short-curve tolerance.</summary>
        private static bool IsChainClosed(List<Curve> chain, double tolFeet)
        {
            if (chain == null || chain.Count < 3) return false;
            XYZ start = chain[0].GetEndPoint(0);
            XYZ end = chain[chain.Count - 1].GetEndPoint(1);
            return start.DistanceTo(end) <= tolFeet;
        }

        /// <summary>Builds a CurveLoop from an already-confirmed-closed chain and
        /// creates a Filled Region on the active view. Looks up a Filled Region Type
        /// by the mapping's own Detail Line Style name first (so a project that names
        /// both the same way gets a matching fill automatically); falls back to the
        /// first Filled Region Type available in the project, with a warning, if no
        /// name match exists. Never throws -- failures are logged and skipped, same
        /// as every other per-element step in this engine.</summary>
        private void TryCreateFilledRegion(
            Document hostDoc, View activeView, Element elem, ElementMapping mapping,
            List<Curve> closedChain, MappingProcessingResult result, Action<string, LogSeverity> log)
        {
            try
            {
                var curveLoop = new CurveLoop();
                foreach (var c in closedChain)
                    curveLoop.Append(c);

                FilledRegionType? frType = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(FilledRegionType))
                    .Cast<FilledRegionType>()
                    .FirstOrDefault(t => string.Equals(t.Name, mapping.DetailLineStyleName, StringComparison.OrdinalIgnoreCase));

                if (frType == null)
                {
                    frType = new FilteredElementCollector(hostDoc)
                        .OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>()
                        .FirstOrDefault();

                    if (frType != null)
                        log($"Element {elem.Id.Value}: no Filled Region Type named '{mapping.DetailLineStyleName}' — used default '{frType.Name}' instead.", LogSeverity.Warning);
                }

                if (frType == null)
                {
                    log($"Element {elem.Id.Value}: no Filled Region Type exists in this project — Filled Region skipped.", LogSeverity.Warning);
                    return;
                }

                FilledRegion.Create(hostDoc, frType.Id, activeView.Id, new List<CurveLoop> { curveLoop });
                result.FilledRegionsCreated++;
                log($"Element {elem.Id.Value}: Filled Region created ('{frType.Name}') for closed outer loop.", LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                log($"Element {elem.Id.Value}: failed to create Filled Region: {ex.Message}", LogSeverity.Warning);
            }
        }

        private static double MmToFeet(double mm) => mm / 304.8;
    }

    public enum LogSeverity { Info, Warning, Error, Success, Debug }

    public class MappingProcessingResult
    {
        public Guid MappingId { get; set; }
        public int ElementsFound { get; set; }
        public int ElementsProcessed { get; set; }
        public int ElementsSkipped { get; set; }
        public int DetailLinesCreated { get; set; }
        public int FilledRegionsCreated { get; set; }
        public List<ProcessingError> Errors { get; set; } = new();

        /// <summary>One entry per created "unit" of Detail Curve elements — a
        /// Profile/Linear line or chain, or one Point-group marker's curve set —
        /// used by the post-run isolated-lines/points cleanup pass (Section 5,
        /// Remove Isolated Lines &amp; Points). Not populated unless that option is
        /// relevant; cheap to always fill in.</summary>
        public List<CreatedElementGroup> CreatedGroups { get; set; } = new();
    }

    /// <summary>One group of Detail Curve element ids created together (a line
    /// chain/loop, or one point marker's curve set), for isolated-element cleanup.</summary>
    public class CreatedElementGroup
    {
        public List<long> ElementIds { get; set; } = new();
        public bool IsPointMarker { get; set; }
    }
}
