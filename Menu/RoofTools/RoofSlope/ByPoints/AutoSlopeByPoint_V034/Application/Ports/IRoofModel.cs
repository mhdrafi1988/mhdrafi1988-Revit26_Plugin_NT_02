// =======================================================
// File: IRoofModel.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
// New in V034. The roof a run works on, as the runner sees it. The Revit
// implementation (Infrastructure/Adapters/RevitRoofModel) holds the
// document, the roof, its shape editor and top face; the runner only gets
// plain data back. One instance per run: Prepare must be called before the
// steps that use the prepared shape points.
// =======================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
{
    /// <summary>The roof being sloped. Hides the Revit document, roof, shape editor and top face.</summary>
    public interface IRoofModel
    {
        /// <summary>Read-only checks before anything changes. Returns null when the run can go
        /// ahead, otherwise the reason to show the user.</summary>
        string CheckReady(AutoSlopeRequest request);

        /// <summary>Id, type, level and offset of the roof, for the log and the result.</summary>
        RoofInfo Describe();

        /// <summary>Resets the shape points, inserts arc points and adds drain points as the
        /// request says. Changes the model (in its own transactions).</summary>
        PreparedRoofData Prepare(AutoSlopeRequest request, Action<RunProgressInfo> progress,
            Action<LogLevel, string> log, CancellationToken cancel);

        /// <summary>Matches the picked points to shape points. Needs <see cref="Prepare"/> first.</summary>
        DrainResolution ResolveDrains(AutoSlopeRequest request, Action<LogLevel, string> log);

        /// <summary>Builds the graph of valid links between shape points. Needs <see cref="Prepare"/> first.</summary>
        PathGraph BuildGraph(AutoSlopeRequest request, Action<double> onProgress, CancellationToken cancel);

        /// <summary>Corners of the roof's outer boundary, for the corner Offset notes. May throw;
        /// the caller treats a failure as "no corner notes".</summary>
        IReadOnlyList<Point3> GetOuterBoundaryCorners(double minAngleDeg);

        /// <summary>Sets each shape point's elevation from its path length. Must run inside an
        /// open change scope.</summary>
        ApplyOutcome ApplyElevations(ShortestPathResult paths, IReadOnlyList<int> drainVertices,
            AutoSlopeRequest request, Action<double> onProgress, CancellationToken cancel);

        /// <summary>Reads the elevations back from the model after the change was committed.</summary>
        ReadbackOutcome ReadBack(IReadOnlyList<VertexData> rows, IReadOnlyList<int> drainVertices);

        /// <summary>Writes the run's values into the roof's parameters (in its own transaction).</summary>
        ParameterWriteOutcome WriteParameters(AutoSlopeRequest request, double highestElevationMm,
            double longestPathFt, int processed, int skipped, int durationSec, int finalDrainCount,
            string runDate, string version);
    }
}
