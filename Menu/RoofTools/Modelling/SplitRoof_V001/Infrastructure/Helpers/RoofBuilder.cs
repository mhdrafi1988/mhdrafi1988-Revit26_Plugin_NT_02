// Split Roof V001 — creates all pieces in one transaction (arcs first, then once as segments); optionally deletes the original.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;
using Edge = Revit26_Plugin_Revised.SplitRoof.V001.Core.Models.Edge;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Infrastructure.Helpers
{
    /// <summary>Creates the split roofs in the document.</summary>
    public static class RoofBuilder
    {
        /// <summary>
        /// Creates one footprint roof per piece with the source roof's type, level and base offset. All pieces (and the optional
        /// deletion of the original) happen in one transaction: any failure rolls everything back. If a piece with arcs is rejected
        /// by Revit the whole set is retried once with arcs flattened to segments. Throws when the final attempt fails.
        /// </summary>
        /// <returns>The new roofs and whether arcs were kept.</returns>
        public static (IReadOnlyList<FootPrintRoof> Roofs, bool UsedArcs) Create(Document doc, FootPrintRoof source,
            IReadOnlyList<SplitPiece> pieces, bool deleteOriginal)
        {
            RoofType type = doc.GetElement(source.GetTypeId()) as RoofType ?? throw new InvalidOperationException("The roof has no roof type.");
            ElementId levelId = source.get_Parameter(BuiltInParameter.ROOF_BASE_LEVEL_PARAM)?.AsElementId() ?? source.LevelId;
            Level level = doc.GetElement(levelId) as Level ?? throw new InvalidOperationException("The roof has no level.");
            double offset = source.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0;

            bool hasArcs = pieces.Any(p => p.Outer.Edges.Concat(p.Holes.SelectMany(h => h.Edges)).Any(e => e.ArcMid != null));
            if (hasArcs)
            {
                try { return (Attempt(doc, source, type, level, offset, pieces, deleteOriginal, true), true); }
                catch (Exception) { /* rolled back inside Attempt; retry with segments */ }
            }
            return (Attempt(doc, source, type, level, offset, pieces, deleteOriginal, false), false);
        }

        private static IReadOnlyList<FootPrintRoof> Attempt(Document doc, FootPrintRoof source, RoofType type, Level level, double offset,
            IReadOnlyList<SplitPiece> pieces, bool deleteOriginal, bool arcs)
        {
            using var t = new Transaction(doc, "Split roof");
            t.Start();
            try
            {
                var created = new List<FootPrintRoof>();
                foreach (SplitPiece piece in pieces)
                {
                    var loops = new List<Loop> { piece.Outer };
                    loops.AddRange(piece.Holes);
                    double z = level.Elevation;
                    var curves = new CurveArray();
                    var order = new List<Edge>();
                    foreach (Loop loop in loops)
                        foreach (Edge e in loop.Edges)
                        {
                            XYZ a = new(e.A.X, e.A.Y, z), b = new(e.B.X, e.B.Y, z);
                            curves.Append(arcs && e.ArcMid is Pt m ? Arc.Create(a, b, new XYZ(m.X, m.Y, z)) : Line.CreateBound(a, b));
                            order.Add(e);
                        }
                    FootPrintRoof roof = doc.Create.NewFootPrintRoof(curves, level, type, out ModelCurveArray map)
                                         ?? throw new InvalidOperationException("Revit returned no roof.");
                    int i = 0;
                    foreach (ModelCurve mc in map)
                    {
                        Edge e = order[i++];
                        if (!e.DefinesSlope) continue;
                        roof.set_DefinesSlope(mc, true);
                        roof.set_SlopeAngle(mc, e.SlopeAngle);
                    }
                    roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM)?.Set(offset);
                    created.Add(roof);
                }
                if (deleteOriginal) doc.Delete(source.Id);
                t.Commit();
                return created;
            }
            catch
            {
                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                throw;
            }
        }
    }
}
