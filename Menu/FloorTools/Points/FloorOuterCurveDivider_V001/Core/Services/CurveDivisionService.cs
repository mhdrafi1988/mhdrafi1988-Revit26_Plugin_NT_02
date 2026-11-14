// =======================================================
// File: CurveDivisionService.cs
// Location: Core/Services/
// Ported unchanged from V002 (namespace updated only).
// Called from CurveDividerEngine, inside the TransactionGroup opened by
// CurveDividerHandler for Apply — this service's own inner Transaction
// nests inside it (standard nested Transaction/TransactionGroup pattern).
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Core.Services
{
    /// <summary>
    /// Extracts non-linear edges from a floor's top face and places exactly
    /// <c>FinalPointCount</c> equally-spaced interior points along each, written into the
    /// floor's <see cref="SlabShapeEditor"/>. N points → N+1 segments; equalization is exact
    /// for arcs and arc-length-accurate for ellipses/splines.
    ///
    /// Logging uses the shared <see cref="LogEntry"/> / <see cref="LogLevel"/> model.
    /// </summary>
    public class CurveDivisionService
    {
        private const int ArcLengthSamples = 512;

        /// <summary>Length-bucket default number of POINTS: ≤4→4, ≤8→8, ≤12→12, else round(L/2).</summary>
        public static int LengthDefaultPoints(double lengthMeters)
        {
            if (lengthMeters <= 4.0)  return 4;
            if (lengthMeters <= 8.0)  return 8;
            if (lengthMeters <= 12.0) return 12;
            return (int)Math.Round(lengthMeters / 2.0, MidpointRounding.AwayFromZero);
        }

        public List<CurveEdgeModel> ExtractNonLinearEdges(Floor floor, out int filteredLineCount)
        {
            filteredLineCount = 0;
            var edges = new List<CurveEdgeModel>();
            if (floor == null) return edges;

            Options opt = new Options { ComputeReferences = true };
            GeometryElement geo = floor.get_Geometry(opt);
            if (geo == null) return edges;

            int idx = 0;
            foreach (GeometryObject obj in geo)
            {
                if (!(obj is Solid solid)) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;
                    if (!pf.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ)) continue;

                    foreach (CurveLoop loop in pf.GetEdgesAsCurveLoops())
                    {
                        foreach (Curve c in loop)
                        {
                            if (c is Line) { filteredLineCount++; continue; }

                            idx++;
                            double lengthM   = UnitUtils.ConvertFromInternalUnits(c.Length, UnitTypeId.Meters);
                            int    defPoints = LengthDefaultPoints(lengthM);

                            edges.Add(new CurveEdgeModel
                            {
                                Index                   = idx,
                                CurveTypeName           = TypeName(c),
                                LengthM                 = lengthM,
                                Geometry                = c,
                                IsSelected              = true,
                                LengthDefaultPointCount = defPoints,
                                PointCount              = defPoints
                            });
                        }
                    }
                }
            }
            return edges;
        }

        public List<LogEntry> ApplyDivisions(Document doc, Floor floor, IEnumerable<CurveEdgeModel> edges)
        {
            var log = new List<LogEntry>();
            void Add(LogLevel lvl, string m) => log.Add(new LogEntry(lvl, m));

            if (doc  == null) { Add(LogLevel.Error, "Document is null."); return log; }
            if (floor == null) { Add(LogLevel.Error, "Floor is null.");     return log; }

            var selected = edges?.Where(e => e != null && e.IsSelected && e.Geometry != null).ToList()
                           ?? new List<CurveEdgeModel>();
            if (!selected.Any()) { Add(LogLevel.Warning, "No edges selected for division."); return log; }

            int grandTotal = 0;

            using (Transaction tx = new Transaction(doc, "Divide Curved Edges"))
            {
                tx.Start();

                SlabShapeEditor editor = floor.GetSlabShapeEditor();
                if (editor == null)
                {
                    Add(LogLevel.Error, "This floor exposes no SlabShapeEditor (shape editing unsupported).");
                    tx.RollBack();
                    return log;
                }
                if (!editor.IsEnabled) editor.Enable();

                foreach (var edge in selected)
                {
                    ToolCancellation.CheckCurrent();
                    try
                    {
                        int n = edge.FinalPointCount;
                        if (n <= 0)
                        {
                            Add(LogLevel.Info, $"Edge {edge.Index} ({edge.CurveTypeName}): rule yields 0 points — skipped.");
                            continue;
                        }

                        List<double> normalizedParams = GetEqualSpacingParams(edge.Geometry, n + 1);

                        int added = 0;
                        foreach (double t in normalizedParams)
                        {
                            XYZ pt = edge.Geometry.Evaluate(t, true);
                            added += TryAddPoint(editor, pt, edge.Index, log);
                        }
                        grandTotal += added;

                        string rule = edge.IsCountDriven
                            ? "count " + n + (edge.HasOverride ? " · override" : edge.IsManual ? " · manual" : " · type/length")
                            : $"{edge.EffectiveTargetMeters:F2} m → {n} pt";
                        double spacingM = UnitUtils.ConvertFromInternalUnits(edge.Geometry.Length / (n + 1), UnitTypeId.Meters);

                        Add(LogLevel.Success, $"Edge {edge.Index} ({edge.CurveTypeName}, {rule}): {added} point(s) @ {spacingM:F3} m equalized spacing.");
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception ex)
                    {
                        Add(LogLevel.Error, $"Edge {edge.Index} ({edge.CurveTypeName}): {ex.Message}");
                    }
                }

                tx.Commit();
            }

            Add(LogLevel.Info, $"Done. {grandTotal} point(s) placed across {selected.Count} edge(s).");
            return log;
        }

        private List<double> GetEqualSpacingParams(Curve curve, int segCount)
        {
            var result = new List<double>();
            if (segCount <= 1) return result;

            if (curve is Arc)
            {
                for (int k = 1; k < segCount; k++) result.Add((double)k / segCount);
                return result;
            }

            double[] t = new double[ArcLengthSamples + 1];
            double[] s = new double[ArcLengthSamples + 1];
            XYZ prev = curve.Evaluate(0.0, true);
            t[0] = 0.0; s[0] = 0.0;
            for (int i = 1; i <= ArcLengthSamples; i++)
            {
                double ti = (double)i / ArcLengthSamples;
                XYZ p = curve.Evaluate(ti, true);
                s[i] = s[i - 1] + p.DistanceTo(prev);
                t[i] = ti;
                prev = p;
            }
            double totalLen = s[ArcLengthSamples];
            if (totalLen < 1e-9) return result;

            for (int k = 1; k < segCount; k++)
                result.Add(NormalizedParamAtLength(t, s, totalLen * k / segCount));
            return result;
        }

        private double NormalizedParamAtLength(double[] t, double[] s, double targetLen)
        {
            int lo = 0, hi = s.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (s[mid] < targetLen) lo = mid + 1; else hi = mid;
            }
            if (lo == 0) return t[0];
            double s0 = s[lo - 1], s1 = s[lo], t0 = t[lo - 1], t1 = t[lo];
            double span = s1 - s0;
            double frac = span < 1e-12 ? 0.0 : (targetLen - s0) / span;
            return t0 + frac * (t1 - t0);
        }

        private int TryAddPoint(SlabShapeEditor editor, XYZ pt, int edgeIdx, List<LogEntry> log)
        {
            try { editor.AddPoint(pt); return 1; }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                log.Add(new LogEntry(LogLevel.Warning, $"Edge {edgeIdx}: point coincides with an existing vertex — skipped."));
                return 0;
            }
        }

        private static string TypeName(Curve c)
        {
            if (c is Arc)              return "Arc";
            if (c is Ellipse)          return "Ellipse";
            if (c is NurbSpline)       return "NurbSpline";
            if (c is HermiteSpline)    return "HermiteSpline";
            if (c is CylindricalHelix) return "Helix";
            return c.GetType().Name;
        }
    }
}
