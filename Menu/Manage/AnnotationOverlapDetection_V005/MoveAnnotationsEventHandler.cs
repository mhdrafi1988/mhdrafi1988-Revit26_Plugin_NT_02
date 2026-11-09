using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005
{
    /// <summary>What the window asks the move handler to do.</summary>
    public sealed class ResolveRequest
    {
        /// <summary>Document the scan came from.</summary>
        public Document Document { get; set; }

        /// <summary>Plan view the annotations live in.</summary>
        public ElementId ViewId { get; set; }

        /// <summary>Overlapping pairs to resolve, in grid order.</summary>
        public List<(long id1, long id2)> Pairs { get; set; } = new();

        /// <summary>Which element(s) move.</summary>
        public ResolveMode Mode { get; set; }

        /// <summary>Clearance left between the two boxes, mm.</summary>
        public double GapMm { get; set; }
    }

    /// <summary>Result of one resolve run, handed back to the window.</summary>
    public sealed class ResolveOutcome
    {
        /// <summary>Status per pair, keyed by <see cref="PairKey"/>.</summary>
        public Dictionary<(long, long), string> PairStatus { get; } = new();

        /// <summary>Pairs whose elements were moved.</summary>
        public int PairsResolved { get; set; }

        /// <summary>Pairs that were skipped or failed.</summary>
        public int PairsSkipped { get; set; }

        /// <summary>Elements actually moved.</summary>
        public int ElementsMoved { get; set; }

        /// <summary>Fresh scan of the view after the run (null when the scan could not run).</summary>
        public List<AnnotationData> Rescan { get; set; }

        /// <summary>Error that stopped the whole run, or null.</summary>
        public string Error { get; set; }

        /// <summary>Order-independent key for a pair.</summary>
        public static (long, long) PairKey(long a, long b) => a <= b ? (a, b) : (b, a);
    }

    /// <summary>
    /// Moves clashing annotations apart (Move Up / Move Down / Distribute). The WPF window cannot
    /// call the Revit API, so it fills <see cref="Request"/> and raises the external event; Revit then
    /// calls <see cref="Execute"/>. All moves of one run are one transaction ("Resolve Annotation
    /// Overlaps"), so one Undo reverts them. Each element moves in its own sub-transaction, so one
    /// refused move does not undo the others.
    /// </summary>
    public class MoveAnnotationsEventHandler : IExternalEventHandler
    {
        private const string TransactionName = "Resolve Annotation Overlaps";

        /// <summary>The run to perform on the next <see cref="Execute"/>.</summary>
        public ResolveRequest Request { get; set; }

        /// <summary>Called on the Revit thread when a run finishes (successfully or not).</summary>
        public Action<ResolveOutcome> Completed { get; set; }

        /// <summary>Entry point called by Revit; delegates to <see cref="ToolGuard.RunHandler"/>.</summary>
        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        /// <inheritdoc />
        public string GetName() => "Resolve Annotation Overlaps";

        private void ExecuteCore(UIApplication app)
        {
            ResolveRequest request = Request;
            Request = null;
            if (request == null)
                return;

            var outcome = new ResolveOutcome();
            try
            {
                Run(request, outcome);
            }
            catch (InvalidOperationException ex)
            {
                // Expected conditions (model closed, read-only, view gone): show in the window only.
                outcome.Error = $"Nothing was moved: {ex.Message}";
            }
            catch (Exception ex)
            {
                outcome.Error = $"Resolve failed and nothing was moved: {ex.Message}";
                ToolGuard.Report(GetType(), ex);
            }
            finally
            {
                // Always hand back a fresh scan, so the grid matches the model even after an error.
                try
                {
                    Document doc = request.Document;
                    if (doc != null && doc.IsValidObject && doc.GetElement(request.ViewId) is View view)
                        outcome.Rescan = OverlapDetectionHelper.GetAnnotationsFromView(view, doc);
                }
                catch (Exception ex)
                {
                    outcome.Error ??= $"Re-scan failed: {ex.Message}";
                }

                Completed?.Invoke(outcome);
            }
        }

        private static void Run(ResolveRequest request, ResolveOutcome outcome)
        {
            Document doc = request.Document;
            if (doc == null || !doc.IsValidObject)
                throw new InvalidOperationException("The model this window was opened on is closed.");
            if (doc.IsReadOnly)
                throw new InvalidOperationException("The model is read-only.");
            if (doc.IsModifiable)
                throw new InvalidOperationException("Revit is busy with another edit. Finish it and try again.");
            if (!(doc.GetElement(request.ViewId) is View view) || !view.IsValidObject)
                throw new InvalidOperationException("The plan view the scan came from no longer exists.");

            // Fresh boxes for every element in the request: the model may have changed since the scan.
            var check = new EditabilityCheck(doc);
            var boxes = new Dictionary<long, ResolveBox>();
            var blocked = new Dictionary<long, string>();
            foreach (long id in request.Pairs.SelectMany(p => new[] { p.id1, p.id2 }).Distinct())
            {
                Element elem = doc.GetElement(new ElementId(id));
                var box = elem == null ? null : BoundingBoxCalculator.GetBoundingBox(elem, view);
                if (box != null)
                    boxes[id] = new ResolveBox { Id = id, X = box.Value.x, Y = box.Value.y, Width = box.Value.width, Height = box.Value.height };

                string reason = check.BlockReason(elem);
                if (reason != null)
                    blocked[id] = reason;
            }

            ResolvePlan plan = OverlapResolver.Plan(boxes, request.Pairs, request.Mode, request.GapMm,
                id => blocked.TryGetValue(id, out var why) ? why : null);

            // Move each element once by its total planned distance.
            var failed = new Dictionary<long, string>();
            XYZ up = view.UpDirection;
            using (var t = new Transaction(doc, TransactionName))
            {
                t.Start();
                foreach (var move in plan.MovesMm)
                {
                    if (Math.Abs(move.Value) < 1e-6)
                        continue;

                    Element elem = doc.GetElement(new ElementId(move.Key));
                    XYZ vector = up.Multiply(BoundingBoxCalculator.MmToFeet(move.Value));
                    string error = MoveOne(doc, elem, vector);
                    if (error == null)
                        outcome.ElementsMoved++;
                    else
                        failed[move.Key] = error;
                }

                if (outcome.ElementsMoved == 0)
                {
                    t.RollBack();
                }
                else if (t.Commit() != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException("Revit did not accept the moves; the transaction was rolled back.");
                }
            }

            foreach (PairPlan p in plan.Pairs)
            {
                string status;
                switch (p.Outcome)
                {
                    case PairOutcome.Planned:
                        string fail = new[] { p.Id1, p.Id2 }
                            .Where(failed.ContainsKey)
                            .Select(id => $"{id} {failed[id]}")
                            .FirstOrDefault();
                        if (fail == null)
                        {
                            status = "Moved";
                            outcome.PairsResolved++;
                        }
                        else
                        {
                            status = $"Failed: {fail}";
                            outcome.PairsSkipped++;
                        }
                        break;
                    case PairOutcome.AlreadyClear:
                        status = "Already clear";
                        outcome.PairsResolved++;
                        break;
                    default:
                        status = $"Skipped: {p.Reason}";
                        outcome.PairsSkipped++;
                        break;
                }
                outcome.PairStatus[ResolveOutcome.PairKey(p.Id1, p.Id2)] = status;
            }
        }

        /// <summary>
        /// Moves one annotation by <paramref name="vector"/> inside its own sub-transaction.
        /// Text notes and tags move as a whole (a tag's leader end stays on its host). A dimension
        /// is moved as a whole where Revit allows it; otherwise only its text moves.
        /// Returns null on success, or a short reason.
        /// </summary>
        private static string MoveOne(Document doc, Element elem, XYZ vector)
        {
            if (elem == null || !elem.IsValidObject)
                return "no longer exists";

            using (var sub = new SubTransaction(doc))
            {
                sub.Start();
                try
                {
                    if (elem is Dimension dim)
                    {
                        if (!TryMoveWhole(doc, dim, vector) && !MoveDimensionText(dim, vector))
                        {
                            sub.RollBack();
                            return "dimension cannot be moved";
                        }
                    }
                    else
                    {
                        ElementTransformUtils.MoveElement(doc, elem.Id, vector);
                    }

                    sub.Commit();
                    return null;
                }
                catch (Exception ex)
                {
                    if (sub.HasStarted() && !sub.HasEnded())
                        sub.RollBack();
                    return ex.Message.Length > 80 ? ex.Message.Substring(0, 80) + "…" : ex.Message;
                }
            }
        }

        private static bool TryMoveWhole(Document doc, Dimension dim, XYZ vector)
        {
            try
            {
                ElementTransformUtils.MoveElement(doc, dim.Id, vector);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Revit refused to move the dimension as a whole (covers its Argument / InvalidOperation exceptions).
                return false;
            }
        }

        private static bool MoveDimensionText(Dimension dim, XYZ vector)
        {
            bool moved = false;
            if (dim.NumberOfSegments > 0)
            {
                foreach (DimensionSegment seg in dim.Segments)
                {
                    if (seg.IsTextPositionAdjustable())
                    {
                        seg.TextPosition = seg.TextPosition.Add(vector);
                        moved = true;
                    }
                }
            }
            else if (dim.IsTextPositionAdjustable())
            {
                dim.TextPosition = dim.TextPosition.Add(vector);
                moved = true;
            }
            return moved;
        }
    }
}
