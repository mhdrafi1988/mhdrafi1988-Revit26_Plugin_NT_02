using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>
    /// Reads the floor's sketch loops (Shared.Services.FloorSketchProfile; a Floor has no GetProfiles())
    /// into plan segments (metres) and validates them
    /// with the Core <see cref="OutlineValidator"/>. Never throws for bad geometry.
    /// </summary>
    public static class FloorFootprintReader
    {
        public static OutlineValidationResult Read(Floor floor, out int sketchEdgeCount)
        {
            var loops = new List<IReadOnlyList<Segment2D>>();
            int nonLinear = 0;
            sketchEdgeCount = 0;

            foreach (List<Curve> loop in FloorSketchProfile.GetLoops(floor))
            {
                var segments = new List<Segment2D>();
                foreach (Curve curve in loop)
                {
                    sketchEdgeCount++;
                    if (curve is Line line && line.IsBound)
                    {
                        segments.Add(new Segment2D(
                            UnitConverter.ToPlanMetres(line.GetEndPoint(0)),
                            UnitConverter.ToPlanMetres(line.GetEndPoint(1))));
                    }
                    else
                    {
                        nonLinear++;
                    }
                }

                loops.Add(segments);
            }

            return OutlineValidator.Validate(loops, nonLinear);
        }
    }
}
