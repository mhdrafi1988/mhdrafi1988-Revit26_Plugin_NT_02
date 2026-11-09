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
    /// Orchestrates the Point-group pipeline for one enabled ElementMapping, per spec
    /// Sections 19-21:
    ///
    ///   Find Matching Linked Elements
    ///     -> Spatial Candidate Filter (Stage 1, same as Profile/Linear)
    ///     -> Classify location (Point / Curve / Unsupported) — MANDATORY per-element
    ///        check, not assumed from category (spec Section 21)
    ///     -> If Point: transform point -> project to plan -> boundary check ->
    ///        build Circle/Rectangle marker curves -> clip marker to boundary
    ///     -> If Curve: routed through the SAME rendering path as Linear-group
    ///        elements (transform/project/clip a single centerline), because a
    ///        mechanical family with a curve location has no different treatment
    ///        than a Wall/Beam once its location kind is known
    ///     -> If Unsupported: skipped, reported in the processing log
    ///     -> Create Detail Curve(s), apply style, apply color, store metadata
    /// </summary>
    public class PointProcessingEngine
    {
        private readonly SpatialFilterService _spatialFilter = new();
        private readonly PointGeometryExtractionService _pointExtraction = new();
        private readonly GeometryExtractionService _curveNormalizer = new();
        private readonly PointMarkerGeometryService _markerGeometry = new();
        private readonly ColumnFootprintService _footprintService = new();
        private readonly GeometryTransformService _transform = new();
        private readonly GeometryProjectionService _projection = new();
        private readonly GeometryClippingService _clipping = new();
        private readonly DetailLineCreationService _lineCreation = new();
        private readonly GraphicsOverrideService _graphicsOverride = new();
        private readonly MetadataService _metadata = new();
        private readonly DetailLineStyleService _lineStyleService = new();

        private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new Dictionary<string, BuiltInCategory>
        {
            ["Structural Column"] = BuiltInCategory.OST_StructuralColumns,
            ["Column"] = BuiltInCategory.OST_Columns,
            ["Mechanical Equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        };

        public MappingProcessingResult ProcessMapping(
            Document hostDoc,
            View activeView,
            RevitLinkInstance linkInstance,
            ElementMapping mapping,
            List<XYZ> processingBoundary,
            ProcessingScope scope,
            ComplexCurveSettings complexCurveSettings,
            CircleMarkerSettings circleSettings,
            RectangleMarkerSettings rectangleSettings,
            ActualProfileSettings actualProfileSettings,
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
                if (!CategoryMap.TryGetValue(mapping.CategoryName, out BuiltInCategory bic))
                {
                    Log($"Mapping '{mapping.CategoryName}/{mapping.TypeName}': unrecognized Point-group category, skipped.", LogSeverity.Error);
                    return result;
                }

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
                        scope, complexCurveSettings, circleSettings, rectangleSettings,
                        actualProfileSettings, lineStyle, planZ, result, Log);
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
            ComplexCurveSettings complexCurveSettings, CircleMarkerSettings circleSettings,
            RectangleMarkerSettings rectangleSettings, ActualProfileSettings actualProfileSettings,
            GraphicsStyle? lineStyle, double planZ, MappingProcessingResult result, Action<string, LogSeverity> log)
        {
            var extraction = _pointExtraction.Extract(elem,
                (msg, id) =>
                {
                    result.ElementsSkipped++;
                    result.Errors.Add(new ProcessingError { ElementId = elem.Id.Value, CategoryName = mapping.CategoryName, Reason = msg });
                    log($"Element {elem.Id.Value} ({mapping.CategoryName}): {msg}", LogSeverity.Warning);
                });

            if (extraction.Kind == ElementLocationKind.Unsupported)
                return; // already logged/counted by Extract's onWarning callback

            List<DetailCurve> createdCurves;
            ColumnFootprint? actualProfile = null;   // set only for ActualProfile rows
            double actualProfileHostRotation = 0.0;

            if (extraction.Kind == ElementLocationKind.Curve)
            {
                // Mechanical (or any Point-category) element with a curve location —
                // per spec Section 21, render through the Linear pipeline logic
                // rather than forcing a point marker it doesn't actually have.
                log($"Element {elem.Id.Value} ({mapping.CategoryName}): classified as Curve location — rendered as Linear, not Point marker.", LogSeverity.Info);

                Curve normalizedCurve = _curveNormalizer.NormalizeSingleCurve(
                    extraction.Curve!, complexCurveSettings, elem.Id,
                    (msg, id) => log($"Element {elem.Id.Value}: {msg}", LogSeverity.Warning));

                Curve hostCurve = _transform.TransformCurves(new List<Curve> { normalizedCurve }, linkToHost)[0];
                Curve planCurve = _projection.ProjectToPlan(new List<Curve> { hostCurve }, planZ)[0];

                LinearClipResult clip = _clipping.ClipLinearCurve(planCurve, processingBoundary, scope.TrimToBoundary);
                foreach (var w in clip.Warnings)
                    log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning);

                if (clip.FullyOutside) return;
                if (clip.Segments.Count == 0) { result.ElementsSkipped++; return; }

                createdCurves = _lineCreation.CreateDetailCurves(hostDoc, activeView, clip.Segments,
                    w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));
            }
            else
            {
                log($"Element {elem.Id.Value} ({mapping.CategoryName}): classified as Point location — rendered as {mapping.Representation} marker.", LogSeverity.Debug);

                // Point location — build marker geometry, transform, project, clip.
                XYZ hostPoint = linkToHost.OfPoint(extraction.Point!);
                XYZ planPoint = new XYZ(hostPoint.X, hostPoint.Y, planZ);

                // ActualProfile: the element's real footprint (true size + rotation),
                // falling back to parameters and then a fixed marker — never null.
                List<Curve>? markerCurves = null;

                if (mapping.Representation == RepresentationMode.ActualProfile)
                {
                    markerCurves = BuildActualProfile(
                        elem, extraction.Point!, linkToHost, planZ, complexCurveSettings,
                        circleSettings, rectangleSettings, actualProfileSettings, log,
                        out XYZ profileCenter, out actualProfile, out actualProfileHostRotation);
                    planPoint = profileCenter;
                }

                if (scope.LimitToActiveView && !PointInPolygon(planPoint, processingBoundary))
                {
                    // Outside processing scope — not an error.
                    return;
                }

                if (markerCurves == null)
                {
                    double rotationRadians = mapping.Representation == RepresentationMode.Rectangle
                        ? ComputeRectangleRotationRadians(rectangleSettings, elem, linkToHost, hostDoc, activeView, complexCurveSettings, log)
                        : 0.0;

                    markerCurves = _markerGeometry.BuildMarker(
                        planPoint, mapping.Representation, circleSettings, rectangleSettings, rotationRadians);
                }

                // Marker is a small closed loop; clip it the same way Profile loops
                // are clipped (it can straddle the boundary edge for markers near
                // the crop line). Point markers aren't inner/outer classified, so they
                // reuse OuterLoopClosing as the closest reasonable default.
                ClipResult clip = _clipping.ClipLoop(markerCurves, processingBoundary, scope.TrimToBoundary, scope.OuterLoopClosing,
                    w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

                if (clip.FullyOutside) return;

                var allSegments = new List<Curve>();
                foreach (var chain in clip.Chains) allSegments.AddRange(chain);

                if (allSegments.Count == 0) { result.ElementsSkipped++; return; }

                createdCurves = _lineCreation.CreateDetailCurves(hostDoc, activeView, allSegments,
                    w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));
            }

            if (createdCurves.Count == 0)
            {
                result.ElementsSkipped++;
                return;
            }

            log($"Element {elem.Id.Value}: {createdCurves.Count} Detail Line(s) created.", LogSeverity.Debug);

            if (actualProfile != null)
                log($"Element {elem.Id.Value}: column profile — {DescribeProfile(actualProfile, actualProfileHostRotation)}.", LogSeverity.Info);

            _lineCreation.ApplyLineStyle(createdCurves, lineStyle,
                w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

            foreach (var dc in createdCurves)
            {
                _graphicsOverride.ApplyColorOverride(hostDoc, activeView, dc.Id, mapping.ColorName,
                    w => log($"Element {elem.Id.Value}: {w}", LogSeverity.Warning));

                if (actualProfile != null)
                    _metadata.WriteProfileMetadata(dc, actualProfile, actualProfileHostRotation);

                _metadata.WriteMetadata(
                    dc,
                    mapping.LinkInstanceId,
                    mapping.LinkDisplayName,
                    mapping.CategoryName,
                    mapping.FamilyName,
                    mapping.TypeName,
                    elem.Id.Value,
                    extraction.Kind == ElementLocationKind.Curve ? "Linear (mechanical fallback)" : mapping.Representation.ToString(),
                    mapping.MappingId);
            }

            result.DetailLinesCreated += createdCurves.Count;
            result.CreatedGroups.Add(new CreatedElementGroup
            {
                ElementIds = createdCurves.Select(c => c.Id.Value).ToList(),
                IsPointMarker = extraction.Kind != ElementLocationKind.Curve
            });
            result.ElementsProcessed++;
        }

        /// <summary>RepresentationMode.ActualProfile — resolves the element's footprint
        /// with a three-step fallback, then transforms it to the host and flattens it
        /// to the view plane:
        ///   1. its real geometry (circle / rectangle / custom outline),
        ///   2. its family size parameters (Diameter, b/h, Width/Depth),
        ///   3. the fixed-size Circle or Rectangle marker chosen in Section 4c.
        /// Every step is aligned to the element's own rotation — geometry carries it
        /// implicitly, steps 2–3 read it from the instance transform — and the link's
        /// own rotation is applied by the transform to host. Never returns null.</summary>
        private List<Curve> BuildActualProfile(
            Element elem, XYZ insertionPoint, Transform linkToHost, double planZ,
            ComplexCurveSettings complexCurveSettings, CircleMarkerSettings circleSettings,
            RectangleMarkerSettings rectangleSettings, ActualProfileSettings actualProfileSettings,
            Action<string, LogSeverity> log,
            out XYZ planCenter, out ColumnFootprint footprint, out double hostRotationRadians)
        {
            string? geometryFailure = null;

            if (!_footprintService.TryExtract(elem, out ColumnFootprint? found, out geometryFailure) || found == null)
            {
                if (_footprintService.TryExtractFromParameters(elem, insertionPoint, out found) && found != null)
                {
                    log($"Element {elem.Id.Value}: no usable geometry ({geometryFailure}) — profile taken from the family's size parameters.", LogSeverity.Warning);
                }
                else
                {
                    found = _footprintService.BuildFixedFootprint(
                        elem, insertionPoint, actualProfileSettings.FallbackShape, circleSettings, rectangleSettings);
                    log($"Element {elem.Id.Value}: no usable geometry ({geometryFailure}) and no size parameters found — used a fixed-size {found.Shape} marker.", LogSeverity.Warning);
                }
            }

            footprint = found!;

            // Rotation in HOST coordinates: the footprint's own axis plus the link's rotation.
            double linkRotation = Math.Atan2(linkToHost.BasisX.Y, linkToHost.BasisX.X);
            hostRotationRadians = footprint.Shape == ColumnFootprintShape.Rectangle
                ? footprint.RotationRadians + linkRotation
                : 0.0;

            double offsetMm = FeetToMm(Math.Sqrt(
                Math.Pow(footprint.Center.X - insertionPoint.X, 2) + Math.Pow(footprint.Center.Y - insertionPoint.Y, 2)));
            if (offsetMm > 1.0)
                log($"Element {elem.Id.Value}: footprint centre is {offsetMm:F0} mm from the insertion point — drawn where the geometry actually is.", LogSeverity.Debug);

            // Only a custom outline can hold spline edges worth simplifying; circles
            // and rectangles are already exact arcs/lines.
            List<Curve> curves = footprint.Shape == ColumnFootprintShape.Polygon
                ? footprint.Curves
                    .Select(c => _curveNormalizer.NormalizeSingleCurve(c, complexCurveSettings, elem.Id,
                        (msg, id) => log($"Element {elem.Id.Value}: {msg}", LogSeverity.Warning)))
                    .ToList()
                : footprint.Curves;

            List<Curve> planCurves = _projection.ProjectToPlan(_transform.TransformCurves(curves, linkToHost), planZ);

            XYZ hostCenter = linkToHost.OfPoint(footprint.Center);
            planCenter = new XYZ(hostCenter.X, hostCenter.Y, planZ);
            return planCurves;
        }

        /// <summary>One-line summary for the run log: shape, size, host rotation and
        /// where it came from (geometry, parameters, or fixed marker).</summary>
        private static string DescribeProfile(ColumnFootprint footprint, double hostRotationRadians)
        {
            string shape = footprint.Shape switch
            {
                ColumnFootprintShape.Circle => $"circle Ø{FeetToMm(footprint.DiameterFeet):F0} mm",
                ColumnFootprintShape.Rectangle =>
                    $"rectangle {FeetToMm(footprint.WidthFeet):F0} x {FeetToMm(footprint.HeightFeet):F0} mm, "
                    + $"rotation {DisplayAngleDegrees(hostRotationRadians):F1}°",
                _ => $"custom outline of {footprint.Curves.Count} edge(s)"
            };

            string source = footprint.Source switch
            {
                ColumnFootprintSource.Geometry => footprint.FromBottomFace ? "from geometry (bottom face)" : "from geometry (top face)",
                ColumnFootprintSource.Parameters => "from family size parameters",
                _ => "fixed-size marker, no profile found"
            };

            return $"{shape} — {source}";
        }

        /// <summary>Degrees in (-90, 90] — a rectangle looks the same after a half
        /// turn, so this is its one canonical rotation.</summary>
        private static double DisplayAngleDegrees(double radians)
        {
            double degrees = radians * 180.0 / Math.PI;
            while (degrees > 90.0) degrees -= 180.0;
            while (degrees <= -90.0) degrees += 180.0;
            return degrees;
        }

        private static double FeetToMm(double feet) => feet * 304.8;

        /// <summary>Resolves RectangleMarkerSettings.AlignmentMode into an actual
        /// rotation angle (radians, host coordinates) for one element. See
        /// RectangleAlignmentMode for what each mode means.</summary>
        private double ComputeRectangleRotationRadians(
            RectangleMarkerSettings settings, Element elem, Transform linkToHost,
            Document hostDoc, View activeView, ComplexCurveSettings complexCurveSettings,
            Action<string, LogSeverity> log)
        {
            switch (settings.AlignmentMode)
            {
                case RectangleAlignmentMode.ProjectAxes:
                    return 0.0;

                case RectangleAlignmentMode.InstanceRotation:
                    if (elem is FamilyInstance fi && fi.Location is LocationPoint lp)
                    {
                        // lp.Rotation is the instance's own rotation in the LINKED
                        // doc's coordinate system; add the link placement's own
                        // rotation (its transformed X axis vs. world X) to get the
                        // final angle in host coordinates.
                        double linkRotation = Math.Atan2(linkToHost.BasisX.Y, linkToHost.BasisX.X);
                        return lp.Rotation + linkRotation;
                    }
                    log($"Element {elem.Id.Value}: Instance Rotation alignment requested but element has no point rotation (not a point-placed FamilyInstance) — falling back to Project Axes.", LogSeverity.Warning);
                    return 0.0;

                case RectangleAlignmentMode.TrueNorth:
                    // Angle between Project North and True North; rotating the
                    // marker by this angle makes its edges parallel to True North.
                    return hostDoc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle;

                case RectangleAlignmentMode.ViewAxes:
                    // View.RightDirection/UpDirection do NOT reflect a rotated crop
                    // region — rotating a plan view's crop only changes CropBox.Transform,
                    // not the view's own orientation vectors. Prefer CropBox.Transform
                    // (when crop is active) so a rotated crop actually changes this mode;
                    // fall back to RightDirection for uncropped views.
                    XYZ right = activeView.CropBoxActive
                        ? activeView.CropBox.Transform.BasisX
                        : activeView.RightDirection;
                    return Math.Atan2(right.Y, right.X);

                case RectangleAlignmentMode.Manual:
                    return settings.ManualAngleDegrees * Math.PI / 180.0;

                case RectangleAlignmentMode.OuterProfileAxis:
                    return ComputeOuterProfileAxisRotationRadians(elem, linkToHost, complexCurveSettings, log);

                default:
                    return 0.0;
            }
        }

        /// <summary>RectangleAlignmentMode.OuterProfileAxis — derives rotation from
        /// the linked element's own footprint rather than its placement or any
        /// project/view axis. See the enum's doc comment for the 3-step algorithm;
        /// this reuses GeometryExtractionService (already used for Profile-group
        /// boundary extraction) to get the element's outer loop in linked-doc
        /// coordinates, then converts the resulting axis to host coordinates the
        /// same way InstanceRotation does.</summary>
        private double ComputeOuterProfileAxisRotationRadians(
            Element elem, Transform linkToHost, ComplexCurveSettings complexCurveSettings,
            Action<string, LogSeverity> log)
        {
            ExtractedProfile? profile = _curveNormalizer.ExtractProfile(
                elem, complexCurveSettings,
                (msg, id) => log($"Element {elem.Id.Value}: Outer Profile Axis alignment — {msg}", LogSeverity.Warning));

            if (profile == null || profile.OuterLoops.Count == 0)
            {
                log($"Element {elem.Id.Value}: Outer Profile Axis alignment requested but no outer profile could be extracted (element has no top face) — falling back to Project Axes.", LogSeverity.Warning);
                return 0.0;
            }

            List<Curve> outerLoop = profile.OuterLoops[0];

            // Step 1 — longest straight segment; fall back to the outer loop's
            // bounding-box longest side if it has no straight (Line) segments at all.
            Line? longestLine = outerLoop.OfType<Line>().OrderByDescending(l => l.Length).FirstOrDefault();
            XYZ direction = longestLine != null
                ? (longestLine.GetEndPoint(1) - longestLine.GetEndPoint(0)).Normalize()
                : LongestBoundingBoxSideDirection(outerLoop);

            // Step 2 — primary axis angle, still in the linked document's own
            // coordinate system at this point.
            double axisInLinkDoc = Math.Atan2(direction.Y, direction.X);

            // Step 3 (Inner Loop Adjustment) has no separate effect here — see the
            // enum doc comment. Convert the single axis to host coordinates.
            double linkRotation = Math.Atan2(linkToHost.BasisX.Y, linkToHost.BasisX.X);
            return axisInLinkDoc + linkRotation;
        }

        /// <summary>Fallback for OuterProfileAxis when the outer loop has no straight
        /// segments (e.g. a circular footprint) — direction of its longer bounding-box side.</summary>
        private XYZ LongestBoundingBoxSideDirection(List<Curve> loop)
        {
            var pts = loop.Select(c => c.GetEndPoint(0)).ToList();
            double width = pts.Max(p => p.X) - pts.Min(p => p.X);
            double height = pts.Max(p => p.Y) - pts.Min(p => p.Y);
            return width >= height ? XYZ.BasisX : XYZ.BasisY;
        }

        private bool PointInPolygon(XYZ p, List<XYZ> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                XYZ pi = polygon[i], pj = polygon[j];
                bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X);
                if (intersect) inside = !inside;
            }
            return inside;
        }
    }
}
