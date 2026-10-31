// =======================================================
// File: IAnnotationPlacer.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
// New in V034. Draws the run's annotations (circle markers, ridge lines) in
// the active view. Both calls must run inside the runner's open change scope.
// =======================================================

using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports
{
    /// <summary>Places circle markers and ridge lines in the active view.</summary>
    public interface IAnnotationPlacer
    {
        /// <summary>Drain, highest-point and offset circles (plus offset notes) for the run.</summary>
        MarkerCounts PlaceMarkers(IReadOnlyList<Point3> drainPoints, IReadOnlyList<VertexData> rows,
            AutoSlopeRequest request, Action<LogLevel, string> log);

        /// <summary>Detail lines along the ridges found between drain groups.</summary>
        RidgeLineOutcome PlaceRidgeLines(IReadOnlyList<Point3> positions, RidgeResult ridges,
            AutoSlopeRequest request, Action<LogLevel, string> log);
    }
}
