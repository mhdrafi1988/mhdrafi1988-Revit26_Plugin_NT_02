// =======================================================
// File: FloorGeometryService.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services
// Changes vs V012:
//   - InitializeFloorGeometry (called at pick time) replaced by
//     ResetShapePoints, called by the engine at the start of each floor's
//     run, inside that floor's TransactionGroup. A failed or cancelled floor
//     therefore rolls the reset back too.
//   - Shape editing is enabled only when it is off.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services
{
    /// <summary>Floor geometry steps of a By Drain run: reset, fixture points, top face.</summary>
    public static class FloorGeometryService
    {
        /// <summary>
        /// Turns shape editing on when needed and sets every shape point to 0.
        /// Must be called outside an open Transaction (it opens its own).
        /// Returns the number of points reset.
        /// </summary>
        public static int ResetShapePoints(Floor floor, Document doc)
        {
            int count = 0;
            using (Transaction tx = new Transaction(doc, "Floor Slope By Drain - Reset Shape Points"))
            {
                tx.Start();

                SlabShapeEditor editor = floor.GetSlabShapeEditor()
                    ?? throw new InvalidOperationException("This floor type does not support shape editing.");

                if (!editor.IsEnabled)
                    editor.Enable();

                foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
                {
                    editor.ModifySubElement(vertex, 0.0);
                    count++;
                }

                tx.Commit();
            }
            return count;
        }

        /// <summary>
        /// Adds a shape point on the top face at each fixture drain's plan position,
        /// unless a point is already there. Opens its own Transaction (call it inside
        /// the run's TransactionGroup, outside any open Transaction). A point Revit
        /// refuses is logged; that drain is then reported as unmatched by the engine.
        /// Returns the number of points added.
        /// </summary>
        /// <param name="floor">The floor being sloped (shape editing already on).</param>
        /// <param name="doc">The floor's document.</param>
        /// <param name="topFace">The floor's current top face.</param>
        /// <param name="fixtureDrains">Selected drains with IsFixture true.</param>
        /// <param name="log">Log sink.</param>
        public static int AddFixtureDrainPoints(Floor floor, Document doc, Face topFace,
            IReadOnlyList<DrainItem> fixtureDrains, Action<LogEntry> log)
        {
            if (floor == null || topFace == null || fixtureDrains == null || fixtureDrains.Count == 0) return 0;

            SlabShapeEditor editor = floor.GetSlabShapeEditor()
                ?? throw new InvalidOperationException("This floor type does not support shape editing.");

            double tolFt = DrainDetectionService.FixtureMatchToleranceMm / 304.8;
            var existing = new List<XYZ>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                if (v?.Position != null) existing.Add(v.Position);

            int added = 0;
            using (Transaction tx = new Transaction(doc, "Floor Slope By Drain - Add Drain Family Points"))
            {
                tx.Start();
                foreach (DrainItem drain in fixtureDrains)
                {
                    XYZ c = drain.CenterPoint;
                    if (c == null) continue;
                    if (existing.Any(p => (p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y) <= tolFt * tolFt))
                        continue; // a shape point is already there

                    XYZ onFace = topFace.Project(c)?.XYZPoint ?? c;
                    try
                    {
                        editor.AddPoint(new XYZ(c.X, c.Y, onFace.Z));
                        existing.Add(c);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke(new LogEntry(LogLevel.Warning,
                            $"Drain family {drain.FixtureName} (Id {drain.ElementId?.Value}): no shape point could be added there ({ex.Message})."));
                    }
                }
                tx.Commit();
            }

            if (added > 0)
                log?.Invoke(new LogEntry(LogLevel.Info, $"Added {added} shape point(s) at drain families."));
            return added;
        }

        /// <summary>The floor face whose centre sits highest.</summary>
        public static Face GetTopFace(Floor floor)
        {
            GeometryElement geomElem = floor.get_Geometry(new Options());
            if (geomElem == null) return null;

            Face topFace = null;
            double maxZ = double.MinValue;

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (face == null) continue;
                        BoundingBoxUV bb = face.GetBoundingBox();
                        if (bb == null) continue;

                        UV midpointUV = new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2);
                        XYZ midpoint = face.Evaluate(midpointUV);

                        if (midpoint != null && midpoint.Z > maxZ)
                        {
                            maxZ = midpoint.Z;
                            topFace = face;
                        }
                    }
                }
            }
            return topFace;
        }
    }
}
