// =======================================================
// File: RevitRoofModel.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
// New in V034. IRoofModel on a real Revit roof. It only wraps the V033 code
// (RoofPreparer, DrainResolver, PathGraphBuilder, ElevationApplier,
// ModelReadback, AutoSlopeParameterWriter, AutoSlopeGeometry) and keeps the
// Revit objects (document, roof, shape editor, top face, arcs) to itself.
// One instance per run: Prepare stores the prepared roof for later steps.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
{
    /// <summary>The roof of one run, backed by the Revit document.</summary>
    internal sealed class RevitRoofModel : IRoofModel
    {
        private readonly Document _doc;
        private readonly long _roofIdValue;
        private RoofBase _roof;
        private PreparedRoof _prep;

        /// <summary>Creates the model for the roof with the given id in <paramref name="doc"/>.</summary>
        public RevitRoofModel(Document doc, long roofIdValue)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _roofIdValue = roofIdValue;
        }

        private RoofBase Roof => _roof
            ?? throw new AutoSlopeAbortException("The roof was not found. It may have been deleted.");

        private PreparedRoof Prepared => _prep
            ?? throw new InvalidOperationException("The roof has not been prepared yet.");

        /// <inheritdoc/>
        public string CheckReady(AutoSlopeRequest request)
        {
            _roof = _doc.GetElement(new ElementId(_roofIdValue)) as RoofBase;
            if (_roof == null)
                return "The roof was not found. It may have been deleted.";
            if (request.PickedPoints == null || request.PickedPoints.Count == 0)
                return "No drain points are picked. Use Re-pick drains first.";

            SlabShapeEditor editor = RoofPreparer.GetEditor(_roof);
            if (!editor.IsEnabled)
                return "Shape editing is off for this roof. Close the window and run the tool again to turn it on.";
            if (RoofPreparer.Collect(editor).Count == 0)
                return "The roof has no shape points.";
            if (AutoSlopeGeometry.GetTopFace(_roof) == null)
                return "The roof's top face could not be found.";
            return null;
        }

        /// <inheritdoc/>
        public RoofInfo Describe() => ProjectQueries.DescribeRoof(_roof);

        /// <inheritdoc/>
        public PreparedRoofData Prepare(AutoSlopeRequest request, Action<RunProgressInfo> progress,
            Action<LogLevel, string> log, CancellationToken cancel)
        {
            _prep = RoofPreparer.Prepare(_doc, Roof, request, progress, log, cancel);
            return new PreparedRoofData
            {
                Positions = _prep.Positions.Select(p => p.ToPoint3()).ToList(),
                ArcsFound = _prep.ArcsFound,
                ArcPointsInserted = _prep.ArcPointsInserted
            };
        }

        /// <inheritdoc/>
        public DrainResolution ResolveDrains(AutoSlopeRequest request, Action<LogLevel, string> log) =>
            DrainResolver.Resolve(request.PickedPoints, Prepared.Positions, Prepared.TopFace, request, log);

        /// <inheritdoc/>
        public PathGraph BuildGraph(AutoSlopeRequest request, Action<double> onProgress, CancellationToken cancel)
        {
            double maxEdgeFt = RevitConvert.MToFt(request.EffectiveMaxEdgeLengthM);
            double curveTolFt = RevitConvert.MmToFt(AutoSlopeConstants.CurveToleranceMm);
            var builder = new PathGraphBuilder(
                Prepared.Positions, Prepared.TopFace, maxEdgeFt, Prepared.Arcs, curveTolFt, onProgress, cancel);
            return builder.Build();
        }

        /// <inheritdoc/>
        public IReadOnlyList<Point3> GetOuterBoundaryCorners(double minAngleDeg) =>
            AutoSlopeGeometry.GetOuterBoundaryCorners(Prepared.TopFace, minAngleDeg)
                .Select(p => p.ToPoint3())
                .ToList();

        /// <inheritdoc/>
        public ApplyOutcome ApplyElevations(ShortestPathResult paths, IReadOnlyList<int> drainVertices,
            AutoSlopeRequest request, Action<double> onProgress, CancellationToken cancel) =>
            ElevationApplier.Apply(Prepared.Editor, Prepared.Vertices, Prepared.Positions,
                paths, drainVertices, request, onProgress, cancel);

        /// <inheritdoc/>
        public ReadbackOutcome ReadBack(IReadOnlyList<VertexData> rows, IReadOnlyList<int> drainVertices) =>
            ModelReadback.Read(Roof, rows, drainVertices);

        /// <inheritdoc/>
        public ParameterWriteOutcome WriteParameters(AutoSlopeRequest request, double highestElevationMm,
            double longestPathFt, int processed, int skipped, int durationSec, int finalDrainCount,
            string runDate, string version) =>
            AutoSlopeParameterWriter.WriteAll(_doc, Roof, request, highestElevationMm, longestPathFt,
                processed, skipped, durationSec, finalDrainCount, runDate, version);
    }
}
