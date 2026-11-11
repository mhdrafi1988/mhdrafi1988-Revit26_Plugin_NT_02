// =======================================================
// File: EdgePointService.cs
// Location: Core/Services/
// Extracts every perimeter edge of a roof (lines, arcs, ellipses,
// splines — all types) and places shape-edit points on each per the
// global MultiplePointsSettings rule:
//   - Midpoint            → t = 1/2 (by arc length)
//   - Quarter points      → t = 1/4, 3/4 (by arc length)
//   - Extra on long edges → when an edge's length exceeds the
//     threshold, additional equally-spaced points are added (on top of
//     midpoint/quarter) so no gap between consecutive points on that
//     edge exceeds the threshold.
// Arc-length parameterization follows the same sampling technique as
// OuterCurveDivider's CurveDivisionService: exact for Line/Arc (whose
// normalized parameter is already proportional to length), sampled for
// everything else (Ellipse, NurbSpline, ...).
//
// Edge extraction: a flat roof's top is one horizontal PlanarFace, but
// once slopes/shape points exist it's several tilted facets meeting at
// hip/ridge/valley lines. Rather than requiring an exact horizontal
// normal (which only matches a still-flat roof), every upward-facing
// facet (FaceNormal.Z > 0 — true at any slope) is scanned, and an edge
// is kept only if it borders exactly one such facet: a hip/ridge/valley
// line borders two top facets (counted twice) and is excluded, while a
// true perimeter/opening edge borders one facet and one side face
// (counted once). This reduces to the old single-face behavior when the
// roof is still flat, and generalizes correctly to any slope percentage.
// =======================================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.MultiplePoints.V003.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.MultiplePoints.V003.Core.Services
{
    public class EdgePointService
    {
        private const int    ArcLengthSamples   = 512;
        private const double FractionTolerance  = 1e-4;
        private const double PointKeyTolerance  = 1e-6;

        public List<EdgePointModel> ExtractEdges(RoofBase roof)
        {
            var edges = new List<EdgePointModel>();
            if (roof == null) return edges;

            Options opt = new Options { ComputeReferences = true };
            GeometryElement geo = roof.get_Geometry(opt);
            if (geo == null) return edges;

            // Every upward-facing planar facet — one face for a flat roof, several
            // tilted ones once slope/shape points exist. SlopePercent is the owning
            // facet's slope, carried onto the edge for the UI's optional max-slope filter.
            var keyOrder = new List<string>();
            var counts   = new Dictionary<string, (int Count, Curve Curve, double SlopePercent)>();

            foreach (GeometryObject obj in geo)
            {
                if (!(obj is Solid solid)) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;
                    if (pf.FaceNormal.Z <= 1e-6) continue; // side/fascia or bottom face — not a roof-top facet

                    double slopePercent = SlopePercentFromNormal(pf.FaceNormal);

                    foreach (CurveLoop loop in pf.GetEdgesAsCurveLoops())
                    {
                        foreach (Curve c in loop)
                        {
                            string key = EdgeKey(c);
                            if (counts.TryGetValue(key, out var existing))
                                counts[key] = (existing.Count + 1, existing.Curve, existing.SlopePercent);
                            else
                            {
                                counts[key] = (1, c, slopePercent);
                                keyOrder.Add(key);
                            }
                        }
                    }
                }
            }

            int idx = 0;
            foreach (string key in keyOrder)
            {
                var entry = counts[key];
                if (entry.Count != 1) continue; // shared between two top facets — a hip/ridge/valley line, not a perimeter edge

                idx++;
                double lengthM = UnitUtils.ConvertFromInternalUnits(entry.Curve.Length, UnitTypeId.Meters);

                edges.Add(new EdgePointModel
                {
                    Index             = idx,
                    CurveTypeName     = TypeName(entry.Curve),
                    LengthM           = lengthM,
                    Geometry          = entry.Curve,
                    FacetSlopePercent = entry.SlopePercent,
                    IsSelected        = true
                });
            }
            return edges;
        }

        /// <summary>Slope (rise/run × 100) of a facet from its normal's Z-component — e.g. Z ≈ 0.995 (≈5.71° from horizontal) is a 10% slope.</summary>
        private static double SlopePercentFromNormal(XYZ normal)
        {
            double z = normal.Z;
            if (z <= 1e-9) return double.PositiveInfinity;
            double horizontal = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
            return 100.0 * horizontal / z;
        }

        /// <summary>Canonical, direction-independent identity for an edge, from its rounded endpoints — used to detect the same physical edge shared by two adjacent top facets.</summary>
        private static string EdgeKey(Curve c)
        {
            string a = RoundPoint(c.GetEndPoint(0));
            string b = RoundPoint(c.GetEndPoint(1));
            return string.CompareOrdinal(a, b) <= 0 ? a + "→" + b : b + "→" + a;
        }

        private static string RoundPoint(XYZ p)
        {
            double Rnd(double v) => Math.Round(v / PointKeyTolerance) * PointKeyTolerance;
            return $"{Rnd(p.X):F6},{Rnd(p.Y):F6},{Rnd(p.Z):F6}";
        }

        /// <summary>Fractions (0..1, by arc length) at which points get placed, given the current settings — used both for Apply and for the grid's live preview count.</summary>
        public List<double> GetTargetFractions(double lengthM, MultiplePointsSettings settings)
        {
            var fractions = new List<double>();
            if (settings.AddMidpoint) fractions.Add(0.5);
            if (settings.AddQuarterPoints) { fractions.Add(0.25); fractions.Add(0.75); }

            if (settings.AddExtraOnLongEdges && settings.ThresholdMeters > 1e-9 && lengthM > settings.ThresholdMeters)
            {
                int segCount = Math.Max(1, (int)Math.Ceiling(lengthM / settings.ThresholdMeters));
                for (int k = 1; k < segCount; k++)
                    fractions.Add((double)k / segCount);
            }

            return MergeClose(fractions);
        }

        public List<LogEntry> ApplyPoints(Document doc, RoofBase roof, IEnumerable<EdgePointModel> edges, MultiplePointsSettings settings)
        {
            var log = new List<LogEntry>();
            void Add(LogLevel lvl, string m) => log.Add(new LogEntry(lvl, m));

            if (doc  == null) { Add(LogLevel.Error, "Document is null.");   return log; }
            if (roof == null) { Add(LogLevel.Error, "Roof is null.");       return log; }

            var selected = edges?.Where(e => e != null && e.IsSelected && e.Geometry != null).ToList()
                           ?? new List<EdgePointModel>();
            if (!selected.Any()) { Add(LogLevel.Warning, "No edges selected."); return log; }

            if (!settings.AddMidpoint && !settings.AddQuarterPoints && !settings.AddExtraOnLongEdges)
            {
                Add(LogLevel.Warning, "No point type is checked — nothing to add.");
                return log;
            }

            int grandTotal = 0;

            using (Transaction tx = new Transaction(doc, "Add Multiple Edge Points"))
            {
                tx.Start();

                SlabShapeEditor editor = roof.GetSlabShapeEditor();
                if (editor == null)
                {
                    Add(LogLevel.Error, "This roof exposes no SlabShapeEditor (shape editing unsupported).");
                    tx.RollBack();
                    return log;
                }
                if (!editor.IsEnabled) editor.Enable();

                foreach (var edge in selected)
                {
                    ToolCancellation.CheckCurrent();
                    try
                    {
                        List<double> fractions = GetTargetFractions(edge.LengthM, settings);
                        if (fractions.Count == 0)
                        {
                            Add(LogLevel.Info, $"Edge {edge.Index} ({edge.CurveTypeName}): no points to place — skipped.");
                            continue;
                        }

                        List<double> normalizedParams = ParamsAtLengthFractions(edge.Geometry, fractions);

                        int added = 0;
                        foreach (double t in normalizedParams)
                        {
                            XYZ pt = edge.Geometry.Evaluate(t, true);
                            added += TryAddPoint(editor, pt, edge.Index, log);
                        }
                        grandTotal += added;

                        Add(LogLevel.Success, $"Edge {edge.Index} ({edge.CurveTypeName}, {edge.LengthM:F3} m): {added} point(s) placed.");
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

        /// <summary>Normalized curve parameters at the given arc-length fractions (0..1). Exact for Line/Arc; sampled for everything else.</summary>
        private List<double> ParamsAtLengthFractions(Curve curve, IEnumerable<double> fractions)
        {
            var fracList = fractions.Where(f => f > 1e-9 && f < 1 - 1e-9).Distinct().OrderBy(f => f).ToList();
            if (fracList.Count == 0) return new List<double>();

            if (curve is Line || curve is Arc)
                return fracList;

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
            if (totalLen < 1e-9) return new List<double>();

            var result = new List<double>();
            foreach (double f in fracList)
                result.Add(NormalizedParamAtLength(t, s, totalLen * f));
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

        /// <summary>Collapses fractions that land within tolerance of each other (e.g. an extra-point split that happens to coincide with a quarter mark).</summary>
        private List<double> MergeClose(List<double> fractions)
        {
            var sorted = fractions.Where(f => f > 1e-9 && f < 1 - 1e-9).Distinct().OrderBy(f => f).ToList();
            var result = new List<double>();
            foreach (double f in sorted)
            {
                if (result.Count == 0 || f - result[result.Count - 1] > FractionTolerance)
                    result.Add(f);
            }
            return result;
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
            if (c is Line)              return "Line";
            if (c is Arc)               return "Arc";
            if (c is Ellipse)           return "Ellipse";
            if (c is NurbSpline)        return "NurbSpline";
            if (c is HermiteSpline)     return "HermiteSpline";
            if (c is CylindricalHelix)  return "Helix";
            return c.GetType().Name;
        }
    }
}
