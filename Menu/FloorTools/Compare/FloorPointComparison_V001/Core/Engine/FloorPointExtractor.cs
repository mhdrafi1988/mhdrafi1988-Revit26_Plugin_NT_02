using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Engine
{
    /// <summary>
    /// Reads shape-editing points from a floor's SlabShapeEditor. Read-only — never
    /// enables shape editing on the floor, since this tool must not mutate either
    /// floor it is comparing. A floor with shape editing not enabled simply has 0
    /// shape-editing points.
    /// </summary>
    public static class FloorPointExtractor
    {
        public static List<FloorPointSample> Extract(Floor floor, Action<LogEntry> log)
        {
            var samples = new List<FloorPointSample>();
            if (floor == null) return samples;

            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            if (editor == null || !editor.IsValidObject)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"'{floor.Name}': slab shape editor is not available — treated as 0 shape-editing points."));
                return samples;
            }

            if (!editor.IsEnabled)
            {
                log?.Invoke(new LogEntry(LogLevel.Info,
                    $"'{floor.Name}': shape editing is not enabled — treated as 0 shape-editing points."));
                return samples;
            }

            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
            {
                samples.Add(new FloorPointSample
                {
                    Position = v.Position,
                    ElevationMm = UnitUtils.ConvertFromInternalUnits(v.Position.Z, UnitTypeId.Millimeters)
                });
            }

            return samples;
        }
    }
}
