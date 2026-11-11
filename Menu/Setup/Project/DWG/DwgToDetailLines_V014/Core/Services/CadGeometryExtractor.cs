// ==============================================
// File: CadGeometryExtractor.cs
// Layer: Core/Services
// Changes vs V010: log delegate switched from Action<string, Brush> to the
// shared Action<LogEntry> used across the rest of the suite.
// ==============================================

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using DBTransform = Autodesk.Revit.DB.Transform;

namespace Revit26_Plugin_Revised.DwgToDetailLines.V014.Core.Services
{
    public static class CadGeometryExtractor
    {
        public record ExtractedCurve(Curve Curve, string Layer);

        /// <summary>
        /// Boundary loop for one hatch/fill region found in the CAD import.
        /// DWG hatches surface in Revit's ImportInstance geometry as Solid
        /// (or planar-faced Mesh) entities rather than curves; the outer
        /// face's edge loop becomes the FilledRegion boundary.
        /// </summary>
        public record ExtractedHatch(CurveLoop Boundary, string Layer);

        /// <summary>
        /// Extracts curves from a linked/imported CAD instance.
        /// SplineHandlingMode.Preserve keeps HermiteSpline/NurbSpline geometry intact
        /// (as a NurbSpline, per Revit's native curve representation).
        /// SplineHandlingMode.Tessellate breaks any spline into straight Line segments
        /// using the curve's tessellated point list.
        /// </summary>
        public static List<ExtractedCurve> Extract(
            ImportInstance import,
            Document doc,
            View view,
            SplineHandlingMode splineMode,
            TransformMethod transformMethod,
            Action<LogEntry> log,
            out int extractionSkipped)
        {
            var result = new List<ExtractedCurve>();

            Options opt = new Options { View = view };

            // DIAGNOSTIC (originated V008): logs all 3 candidate origins for
            // reference, regardless of which method is actually applied below
            // (selected via the Transform Method radio buttons in the UI).
            // Remove this whole block, the TransformMethod enum/UI, and the
            // branch below once the correct method is confirmed against a
            // real DWG.
            XYZ originNone = XYZ.Zero;
            XYZ originGetTransform = import.GetTransform().OfPoint(XYZ.Zero);
            XYZ originGetTotalTransform = import.GetTotalTransform().OfPoint(XYZ.Zero);
            log?.Invoke(new LogEntry(LogLevel.Info,
                $"Transform Method = {transformMethod} | origin candidates — " +
                $"None:({originNone.X:F2},{originNone.Y:F2},{originNone.Z:F2}) " +
                $"GetTransform:({originGetTransform.X:F2},{originGetTransform.Y:F2},{originGetTransform.Z:F2}) " +
                $"GetTotalTransform:({originGetTotalTransform.X:F2},{originGetTotalTransform.Y:F2},{originGetTotalTransform.Z:F2})"));

            DBTransform outer = ResolveOuterTransform(import, transformMethod);

            int splineCount = 0;
            int skipped = 0;

            foreach (GeometryObject g in import.get_Geometry(opt))
            {
                ToolCancellation.CheckCurrent();
                if (g is GeometryInstance gi)
                {
                    DBTransform t = outer.Multiply(gi.Transform);

                    foreach (GeometryObject o in gi.GetInstanceGeometry())
                    {
                        ToolCancellation.CheckCurrent();
                        string layer = ResolveLayer(o, doc);

                        if (o is Curve c)
                        {
                            AddCurve(result, c, t, layer, splineMode, log, ref splineCount, ref skipped);
                        }
                        else if (o is PolyLine pl)
                        {
                            var pts = pl.GetCoordinates();
                            for (int i = 0; i < pts.Count - 1; i++)
                            {
                                ToolCancellation.CheckCurrent();
                                // Degenerate (near-coincident) polyline segments throw
                                // inside Line.CreateBound before any downstream tolerance
                                // filter runs. Isolate per-segment so one bad segment
                                // doesn't abort the whole extraction pass.
                                try
                                {
                                    result.Add(new(
                                        Line.CreateBound(
                                            t.OfPoint(pts[i]),
                                            t.OfPoint(pts[i + 1])),
                                        layer));
                                }
                                catch (Autodesk.Revit.Exceptions.ArgumentException)
                                {
                                    skipped++;
                                    log?.Invoke(new LogEntry(LogLevel.Warning,
                                        $"Layer '{layer}': polyline segment skipped (degenerate/too-short geometry)"));
                                }
                            }
                        }
                    }
                }
            }

            log?.Invoke(new LogEntry(LogLevel.Info, $"Extracted {result.Count} curves"));

            if (splineCount > 0)
            {
                string modeText = splineMode == SplineHandlingMode.Preserve
                    ? "preserved as NurbSpline"
                    : "tessellated to line segments";
                log?.Invoke(new LogEntry(LogLevel.Info, $"{splineCount} spline curve(s) {modeText}"));
            }

            if (skipped > 0)
            {
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"{skipped} entity(ies) skipped during extraction (degenerate/too-short geometry)"));
            }

            extractionSkipped = skipped;
            return result;
        }

        /// <summary>
        /// Extracts hatch/fill boundary loops from a linked/imported CAD instance.
        /// DWG hatches import as Solid geometry (extruded to a nominal thickness by
        /// the DWG importer) or as planar Mesh; in both cases the outward-facing
        /// planar face's outer edge loop is taken as the FilledRegion boundary.
        /// Faces with more than one loop (hatch with an island/hole) use only the
        /// outer loop — inner loops are logged as skipped, not fatal.
        /// </summary>
        public static List<ExtractedHatch> ExtractHatches(
            ImportInstance import,
            Document doc,
            View view,
            TransformMethod transformMethod,
            Action<LogEntry> log)
        {
            var result = new List<ExtractedHatch>();

            Options opt = new Options { View = view };
            DBTransform outer = ResolveOuterTransform(import, transformMethod);

            int innerLoopsSkipped = 0;

            foreach (GeometryObject g in import.get_Geometry(opt))
            {
                ToolCancellation.CheckCurrent();
                if (g is not GeometryInstance gi)
                    continue;

                DBTransform t = outer.Multiply(gi.Transform);

                foreach (GeometryObject o in gi.GetInstanceGeometry())
                {
                    ToolCancellation.CheckCurrent();
                    string layer = ResolveLayer(o, doc);

                    if (o is Solid solid && solid.Faces.Size > 0)
                    {
                        ExtractFaceLoops(solid, t, layer, result, ref innerLoopsSkipped);
                    }
                    else if (o is Mesh mesh && mesh.NumTriangles > 0)
                    {
                        // Meshes have no native EdgeLoop API; no boundary
                        // reconstruction from raw triangles. Logged, not
                        // thrown — mesh-based hatches are simply skipped.
                        log?.Invoke(new LogEntry(LogLevel.Warning,
                            $"Layer '{layer}': mesh-based hatch geometry not supported, skipped"));
                    }
                }
            }

            log?.Invoke(new LogEntry(LogLevel.Info, $"Extracted {result.Count} hatch boundary loop(s)"));

            if (innerLoopsSkipped > 0)
                log?.Invoke(new LogEntry(LogLevel.Warning,
                    $"{innerLoopsSkipped} inner hatch loop(s) (islands) skipped — outer boundary only"));

            return result;
        }

        private static void ExtractFaceLoops(
            Solid solid,
            DBTransform t,
            string layer,
            List<ExtractedHatch> result,
            ref int innerLoopsSkipped)
        {
            // Use the largest-area planar face as the hatch's representative
            // face (the DWG importer extrudes flat hatches to a thin solid;
            // the top or bottom face carries the true boundary shape).
            PlanarFace bestFace = null;
            double bestArea = 0;

            foreach (Face f in solid.Faces)
            {
                ToolCancellation.CheckCurrent();
                if (f is PlanarFace pf && pf.Area > bestArea)
                {
                    bestFace = pf;
                    bestArea = pf.Area;
                }
            }

            if (bestFace == null)
                return;

            IList<CurveLoop> loops = bestFace.GetEdgesAsCurveLoops();
            if (loops.Count == 0)
                return;

            // Outer loop = largest by unsigned area (Revit does not guarantee
            // loops[0] is outer for imported/faceted geometry).
            CurveLoop outer = loops[0];
            double outerArea = LoopArea(outer);

            for (int i = 1; i < loops.Count; i++)
            {
                ToolCancellation.CheckCurrent();
                double a = LoopArea(loops[i]);
                if (a > outerArea)
                {
                    outer = loops[i];
                    outerArea = a;
                }
            }

            innerLoopsSkipped += loops.Count - 1;

            CurveLoop transformed = CurveLoop.CreateViaTransform(outer, t);
            result.Add(new ExtractedHatch(transformed, layer));
        }

        private static double LoopArea(CurveLoop loop)
        {
            try
            {
                return ApproximatePlanarLoopArea(loop);
            }
            catch
            {
                return 0;
            }
        }

        // CurveLoop has no direct .Area; approximate via the shoelace formula
        // projected onto the loop's own plane normal, which is sufficient for
        // outer-vs-inner comparison (relative magnitude only, not used for
        // final geometry).
        private static double ApproximatePlanarLoopArea(CurveLoop loop)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop)
                pts.Add(c.GetEndPoint(0));

            if (pts.Count < 3)
                return 0;

            XYZ normal = loop.GetPlane()?.Normal ?? XYZ.BasisZ;
            double sum = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                ToolCancellation.CheckCurrent();
                XYZ a = pts[i];
                XYZ b = pts[(i + 1) % pts.Count];
                sum += (a.CrossProduct(b)).DotProduct(normal);
            }
            return System.Math.Abs(sum) / 2.0;
        }

        private static void AddCurve(
            List<ExtractedCurve> result,
            Curve c,
            DBTransform t,
            string layer,
            SplineHandlingMode splineMode,
            Action<LogEntry> log,
            ref int splineCount,
            ref int skipped)
        {
            bool isSpline = c is HermiteSpline || c is NurbSpline;

            if (!isSpline)
            {
                // Curve.CreateTransformed throws ArgumentException ("Curve length
                // is too small for Revit's tolerance... Parameter name: endpoints")
                // for entities that are already degenerate in the DWG's own units,
                // BEFORE the tol filter in RunLinePass ever sees them. Isolate here
                // so one bad entity doesn't abort extraction for the whole import.
                try
                {
                    result.Add(new(c.CreateTransformed(t), layer));
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    skipped++;
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Layer '{layer}': entity skipped (degenerate/too-short geometry)"));
                }
                return;
            }

            splineCount++;

            if (splineMode == SplineHandlingMode.Preserve)
            {
                // Ensure we hand back a NurbSpline (Revit's native curve for
                // detail-curve creation); HermiteSpline is converted via its
                // control/tangent data through CreateTransformed, which Revit
                // resolves internally to an equivalent bound curve.
                try
                {
                    result.Add(new(c.CreateTransformed(t), layer));
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    skipped++;
                    log?.Invoke(new LogEntry(LogLevel.Warning,
                        $"Layer '{layer}': spline skipped (degenerate/too-short geometry)"));
                }
            }
            else
            {
                // Tessellate: break into straight segments using Revit's
                // internal tessellation (respects curve tolerance).
                IList<XYZ> pts = c.Tessellate();

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    ToolCancellation.CheckCurrent();
                    XYZ p0 = t.OfPoint(pts[i]);
                    XYZ p1 = t.OfPoint(pts[i + 1]);

                    if (p0.IsAlmostEqualTo(p1))
                        continue;

                    try
                    {
                        result.Add(new(Line.CreateBound(p0, p1), layer));
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        skipped++;
                        log?.Invoke(new LogEntry(LogLevel.Warning,
                            $"Layer '{layer}': tessellated segment skipped (degenerate/too-short geometry)"));
                    }
                }
            }
        }

        /// <summary>
        /// Lightweight pre-scan for the Metrics Card: counts raw geometry entities
        /// and distinct layers in a CAD import, without building full curve output.
        /// Used to populate "CAD Import Info" immediately on selection.
        /// </summary>
        public static (int entityCount, int layerCount) PreScan(
            ImportInstance import,
            Document doc,
            View view)
        {
            Options opt = new Options { View = view };
            var layers = new HashSet<string>();
            int entityCount = 0;

            foreach (GeometryObject g in import.get_Geometry(opt))
            {
                ToolCancellation.CheckCurrent();
                if (g is GeometryInstance gi)
                {
                    foreach (GeometryObject o in gi.GetInstanceGeometry())
                    {
                        ToolCancellation.CheckCurrent();
                        if (o is Curve || o is PolyLine || o is Solid || o is Mesh)
                        {
                            entityCount++;
                            layers.Add(ResolveLayer(o, doc));
                        }
                    }
                }
            }

            return (entityCount, layers.Count);
        }

        /// <summary>
        /// Builds one LayerRow per distinct (layer, entity type) pair found in
        /// the CAD import, for the Layers &amp; Hatches To Convert grid.
        /// Solid/Mesh geometry on a layer marks it as Hatch; Curve/PolyLine
        /// marks it as Line. A layer name appearing as both is listed as two
        /// separate rows (one per type), consistent with the grid's grouping.
        /// </summary>
        public static List<LayerRow> ScanLayers(
            ImportInstance import,
            Document doc,
            View view)
        {
            Options opt = new Options { View = view };

            var lineCounts = new Dictionary<string, int>();
            var hatchCounts = new Dictionary<string, int>();

            foreach (GeometryObject g in import.get_Geometry(opt))
            {
                ToolCancellation.CheckCurrent();
                if (g is not GeometryInstance gi)
                    continue;

                foreach (GeometryObject o in gi.GetInstanceGeometry())
                {
                    ToolCancellation.CheckCurrent();
                    string layer = ResolveLayer(o, doc);

                    if (o is Curve || o is PolyLine)
                    {
                        lineCounts.TryGetValue(layer, out int n);
                        lineCounts[layer] = n + 1;
                    }
                    else if (o is Solid s && s.Faces.Size > 0)
                    {
                        hatchCounts.TryGetValue(layer, out int n);
                        hatchCounts[layer] = n + 1;
                    }
                    else if (o is Mesh)
                    {
                        hatchCounts.TryGetValue(layer, out int n);
                        hatchCounts[layer] = n + 1;
                    }
                }
            }

            var rows = new List<LayerRow>();

            foreach (var kv in lineCounts)
                rows.Add(new LayerRow
                {
                    LayerName = kv.Key,
                    EntityType = CadEntityType.Line,
                    Count = kv.Value
                });

            foreach (var kv in hatchCounts)
                rows.Add(new LayerRow
                {
                    LayerName = kv.Key,
                    EntityType = CadEntityType.Hatch,
                    Count = kv.Value
                });

            return rows;
        }

        private static DBTransform ResolveOuterTransform(ImportInstance import, TransformMethod method)
        {
            return method switch
            {
                TransformMethod.GetTransform => import.GetTransform(),
                TransformMethod.GetTotalTransform => import.GetTotalTransform(),
                _ => DBTransform.Identity
            };
        }

        private static string ResolveLayer(GeometryObject o, Document d)
        {
            if (o.GraphicsStyleId == ElementId.InvalidElementId)
                return "DWG-Default";

            return (d.GetElement(o.GraphicsStyleId) as GraphicsStyle)?
                .GraphicsStyleCategory?.Name ?? "DWG-Default";
        }
    }
}
