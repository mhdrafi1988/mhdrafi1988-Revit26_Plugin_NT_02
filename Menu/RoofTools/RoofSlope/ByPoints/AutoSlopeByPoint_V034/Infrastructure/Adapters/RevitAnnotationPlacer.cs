// =======================================================
// File: RevitAnnotationPlacer.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
// New in V034. IAnnotationPlacer in Revit: wraps CircleMarkerService and
// RidgeLineService. The active view is read at the moment of each call, as
// V033's runner did.
// =======================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Application.Ports;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Adapters
{
    /// <summary>Draws circle markers and ridge lines in the active view of a Revit document.</summary>
    internal sealed class RevitAnnotationPlacer : IAnnotationPlacer
    {
        private readonly UIDocument _uidoc;

        /// <summary>Creates a placer for the document shown in <paramref name="uidoc"/>.</summary>
        public RevitAnnotationPlacer(UIDocument uidoc) =>
            _uidoc = uidoc ?? throw new ArgumentNullException(nameof(uidoc));

        /// <inheritdoc/>
        public MarkerCounts PlaceMarkers(IReadOnlyList<Point3> drainPoints, IReadOnlyList<VertexData> rows,
            AutoSlopeRequest request, Action<LogLevel, string> log) =>
            CircleMarkerService.PlaceMarkers(_uidoc.Document, _uidoc.ActiveView, drainPoints, rows, request, log);

        /// <inheritdoc/>
        public RidgeLineOutcome PlaceRidgeLines(IReadOnlyList<Point3> positions, RidgeResult ridges,
            AutoSlopeRequest request, Action<LogLevel, string> log) =>
            RidgeLineService.Place(_uidoc.Document, _uidoc.ActiveView, positions, ridges, request, log);
    }
}
