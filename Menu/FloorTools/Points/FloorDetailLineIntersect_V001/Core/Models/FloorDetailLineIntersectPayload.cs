// =======================================================
// File: FloorDetailLineIntersectPayload.cs
// Location: Core/Models/
// New in V012. Carries the request across the ExternalEvent boundary.
// V008 held Document/Floor/DetailLines directly on a private
// inner IExternalEventHandler class instead of a dedicated payload —
// functionally fine (the window never re-opens without them), but this
// matches the Payload/Result/Engine convention used by every other tool
// in the suite.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Models
{
    public class FloorDetailLineIntersectPayload
    {
        public ElementId FloorId { get; set; }
        public List<ElementId> DetailLineIds { get; set; }
        public bool IncludeLineToLineIntersections { get; set; } = true;

        public Action<LogEntry> Log { get; set; }
        public Action<FloorDetailLineIntersectResult> OnCompleted { get; set; }
    }
}
