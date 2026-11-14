// =======================================================
// File: RoofPreparer.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// First model-changing step of a run (split out of V030's AutoSlopeEngine):
//   1. reset every shape point to 0
//   2. optionally insert points where real edges meet arcs
//   3. optionally add a shape point at each pick (Drain mode "Add", D2)
// Each step is its own Transaction inside the handler's TransactionGroup;
// any failure later in the run rolls all of them back (B1).
// The SlabShapeEditor handle is re-fetched after every commit (a commit
// regenerates the document and invalidates the old handle).
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    internal sealed class PreparedRoof
    {
        public SlabShapeEditor Editor { get; set; }
        public List<SlabShapeVertex> Vertices { get; set; }
        public List<XYZ> Positions { get; set; }
        public Face TopFace { get; set; }
        /// <summary>Arcs for path handling; empty when curve handling is off.</summary>
        public List<Arc> Arcs { get; set; } = new List<Arc>();
        public int ArcsFound { get; set; }
        public int ArcPointsInserted { get; set; }
        public int DrainPointsAdded { get; set; }
    }

    internal static class RoofPreparer
    {
        public static PreparedRoof Prepare(
            Document doc,
            RoofBase roof,
            AutoSlopeRequest request,
            Action<RunProgressInfo> progress,
            Action<LogLevel, string> log,
            CancellationToken cancel)
        {
            var prepared = new PreparedRoof();

            // ── 1. Reset ───────────────────────────────────────────────────
            progress?.Invoke(RunPhases.Report(RunPhase.ResettingPoints));
            SlabShapeEditor editor = GetEditor(roof);
            using (var tx = new Transaction(doc, "Reset Roof Shape Points"))
            {
                tx.Start();
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                    editor.ModifySubElement(v, 0);
                tx.Commit();
            }
            cancel.ThrowIfCancellationRequested();

            Face topFace = AutoSlopeGeometry.GetTopFace(roof)
                ?? throw new AutoSlopeAbortException("The roof's top face could not be found.");

            // ── 2. Arc points ──────────────────────────────────────────────
            double curveTolFt = RevitConvert.MmToFt(AutoSlopeConstants.CurveToleranceMm);
            if (request.InsertCurveIntersectionPoints)
            {
                progress?.Invoke(RunPhases.Report(RunPhase.InsertingArcPoints));
                List<Arc> arcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);
                prepared.ArcsFound = arcs.Count;

                if (arcs.Count == 0)
                {
                    log(LogLevel.Info, "Curve handling: no arc edges on this roof.");
                }
                else
                {
                    editor = GetEditor(roof);
                    var positions = Collect(editor).Select(v => v.Position).ToList();
                    List<Line> lines = AutoSlopeGeometry.GetBoundaryLines(topFace);

                    int inserted;
                    using (var tx = new Transaction(doc, "Insert Curve Intersection Points"))
                    {
                        tx.Start();
                        inserted = CurveIntersectionHelper.InsertIntersectionPoints(editor, lines, positions, arcs, curveTolFt);
                        tx.Commit();
                    }
                    prepared.ArcPointsInserted = inserted;
                    log(LogLevel.Info, $"Curve handling: {arcs.Count} arc(s) found, {inserted} point(s) inserted on real edges.");

                    if (inserted > 0)
                    {
                        topFace = AutoSlopeGeometry.GetTopFace(roof)
                            ?? throw new AutoSlopeAbortException("The roof's top face could not be found after inserting arc points.");
                    }
                }
                prepared.Arcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);
            }
            cancel.ThrowIfCancellationRequested();

            // ── 3. Drain points at picks (Drain mode "Add a shape point") ──
            if (request.DrainMode == DrainMode.AddPointAtPick)
            {
                progress?.Invoke(RunPhases.Report(RunPhase.AddingDrainPoints));
                prepared.DrainPointsAdded = AddDrainPoints(doc, roof, topFace, request.PickedPoints, log);
                if (prepared.DrainPointsAdded > 0)
                {
                    topFace = AutoSlopeGeometry.GetTopFace(roof)
                        ?? throw new AutoSlopeAbortException("The roof's top face could not be found after adding drain points.");
                    if (request.InsertCurveIntersectionPoints)
                        prepared.Arcs = AutoSlopeGeometry.GetBoundaryArcs(topFace);
                }
            }

            // ── Final state ────────────────────────────────────────────────
            prepared.Editor = GetEditor(roof);
            prepared.Vertices = Collect(prepared.Editor);
            prepared.Positions = prepared.Vertices.Select(v => v.Position).ToList();
            prepared.TopFace = topFace;

            if (prepared.Vertices.Count == 0)
                throw new AutoSlopeAbortException("The roof has no shape points.");

            return prepared;
        }

        public static SlabShapeEditor GetEditor(RoofBase roof)
        {
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor == null || !editor.IsValidObject)
                throw new AutoSlopeAbortException("The roof's shape editor is not available.");
            return editor;
        }

        public static List<SlabShapeVertex> Collect(SlabShapeEditor editor)
        {
            var list = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                list.Add(v);
            return list;
        }

        private static int AddDrainPoints(
            Document doc, RoofBase roof, Face topFace, IReadOnlyList<Point3> picks, Action<LogLevel, string> log)
        {
            SlabShapeEditor editor = GetEditor(roof);
            var existing = Collect(editor).Select(v => v.Position.ToPoint3()).ToList();
            double matchFt = RevitConvert.MmToFt(AutoSlopeConstants.AddedPointMatchMm);
            double zGuess = existing.Count > 0 ? existing.Average(p => p.Z) : 0;

            int added = 0;
            using (var tx = new Transaction(doc, "Add Drain Points"))
            {
                tx.Start();
                for (int k = 0; k < picks.Count; k++)
                {
                    Point3 pick = picks[k];
                    if (existing.Any(p => p.DistanceXY(pick) <= matchFt)) continue; // already a shape point there

                    // Place the point on the (now flat) top surface at the pick's plan position.
                    XYZ guess = new XYZ(pick.X, pick.Y, zGuess);
                    XYZ onFace = topFace.Project(guess)?.XYZPoint ?? guess;
                    try
                    {
                        editor.AddPoint(onFace);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        log(LogLevel.Warning, $"Pick {k + 1}: could not add a shape point there ({ex.Message}). The nearest existing point is used instead.");
                    }
                }
                tx.Commit();
            }

            if (added > 0)
                log(LogLevel.Info, $"Added {added} shape point(s) at the picked drain locations.");
            return added;
        }
    }
}
