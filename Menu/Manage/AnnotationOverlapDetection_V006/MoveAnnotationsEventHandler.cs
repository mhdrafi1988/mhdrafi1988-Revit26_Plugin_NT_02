using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006
{
    /// <summary>How dimensions are moved (6.1.0).</summary>
    public enum DimensionHandling
    {
        /// <summary>Move the whole dimension where Revit allows it, otherwise only its text (default, as 6.0.0).</summary>
        WholeThenText = 0,

        /// <summary>Move only the dimension text; the dimension line stays.</summary>
        TextOnly = 1,

        /// <summary>Never move dimensions (pairs that need one to move are skipped).</summary>
        Never = 2
    }

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

        /// <summary>V006, By priority: categories highest priority first.</summary>
        public List<AnnotationCategory> PriorityOrder { get; set; } = new();

        /// <summary>V006, By priority: categories that never move.</summary>
        public HashSet<AnnotationCategory> NeverMove { get; set; } = new();

        /// <summary>V006, By priority: same-priority rule.</summary>
        public TieRule Tie { get; set; } = TieRule.Distribute;

        /// <summary>6.1.0: move left / right (along the view's right direction) instead of up / down.</summary>
        public bool Horizontal { get; set; }

        /// <summary>6.1.0: how dimensions are moved.</summary>
        public DimensionHandling Dimensions { get; set; } = DimensionHandling.WholeThenText;

        /// <summary>6.1.0: select the moved elements in Revit after the run.</summary>
        public bool SelectMoved { get; set; }
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

        /// <summary>Ids of the elements that moved (6.1.0).</summary>
        public List<long> MovedIds { get; } = new();

        /// <summary>Error that stopped the whole run, or null.</summary>
        public string Error { get; set; }

        /// <summary>Order-independent key for a pair.</summary>
        public static (long, long) PairKey(long a, long b) => a <= b ? (a, b) : (b, a);
    }

    /// <summary>
    /// Moves clashing annotations apart (Move Up / Move Down / Distribute / By priority), vertically or,
    /// since 6.1.0, horizontally. The WPF window cannot
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
                Run(app, request, outcome);
            }
            catch (OperationCanceledException)
            {
                // A user cancel is not an error: the transaction was never committed, so nothing moved.
                outcome.Error = "Cancelled by user. Nothing was moved.";
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

                if (request.SelectMoved && outcome.MovedIds.Count > 0 && outcome.Error == null)
                {
                    try
                    {
                        UIDocument uiDoc = app.ActiveUIDocument;
                        if (uiDoc != null && uiDoc.Document.Equals(request.Document))
                            uiDoc.Selection.SetElementIds(outcome.MovedIds.Select(id => new ElementId(id)).ToList());
                    }
                    catch (Exception)
                    {
                        // Selecting is a convenience: the moves are already committed.
                    }
                }

                Completed?.Invoke(outcome);
            }
        }

        private static void Run(UIApplication app, ResolveRequest request, ResolveOutcome outcome)
        {
            // Esc / Cancel with a Yes-No confirm while the run works; disposed before the re-scan.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AnnotationOverlapDetectionV006, app, "Resolving overlaps…");

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
            var categories = new Dictionary<long, AnnotationCategory>();
            foreach (long id in request.Pairs.SelectMany(p => new[] { p.id1, p.id2 }).Distinct())
            {
                cancel.Check();
                Element elem = doc.GetElement(new ElementId(id));
                var box = elem == null ? null : BoundingBoxCalculator.GetBoundingBox(elem, view);
                if (box != null)
                    boxes[id] = request.Horizontal
                        // Horizontal: the resolver's "up" axis is the view's right direction.
                        ? new ResolveBox { Id = id, X = box.Value.y, Y = box.Value.x, Width = box.Value.height, Height = box.Value.width }
                        : new ResolveBox { Id = id, X = box.Value.x, Y = box.Value.y, Width = box.Value.width, Height = box.Value.height };
                if (elem != null)
                    categories[id] = OverlapDetectionHelper.Classify(elem);

                string reason = check.BlockReason(elem);
                if (reason == null && elem is Dimension && request.Dimensions == DimensionHandling.Never)
                    reason = "dimension (set to never move)";
                if (reason != null)
                    blocked[id] = reason;
            }

            PriorityOptions priority = null;
            if (request.Mode == ResolveMode.ByPriority)
            {
                var order = request.PriorityOrder ?? new List<AnnotationCategory>();
                priority = new PriorityOptions
                {
                    // Unknown category ranks last, so it moves first.
                    Rank = id => categories.TryGetValue(id, out var c) && order.Contains(c) ? order.IndexOf(c) : order.Count,
                    NeverMove = id => categories.TryGetValue(id, out var c) && request.NeverMove != null && request.NeverMove.Contains(c),
                    Tie = request.Tie
                };
            }

            ResolvePlan plan = OverlapResolver.Plan(boxes, request.Pairs, request.Mode, request.GapMm,
                id => blocked.TryGetValue(id, out var why) ? why : null, priority);

            // Move each element once by its total planned distance.
            var failed = new Dictionary<long, string>();
            XYZ up = request.Horizontal ? view.RightDirection : view.UpDirection;
            using (var t = new Transaction(doc, TransactionName))
            {
                t.Start();
                foreach (var move in plan.MovesMm)
                {
                    cancel.Check();
                    if (Math.Abs(move.Value) < 1e-6)
                        continue;

                    Element elem = doc.GetElement(new ElementId(move.Key));
                    XYZ vector = up.Multiply(BoundingBoxCalculator.MmToFeet(move.Value));
                    string error = MoveOne(doc, elem, vector, request.Dimensions);
                    if (error == null)
                    {
                        outcome.ElementsMoved++;
                        outcome.MovedIds.Add(move.Key);
                    }
                    else
                        failed[move.Key] = error;
                }

                cancel.Check(); // last chance to cancel: nothing is committed yet
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
                            status = "Moved " + string.Join(" + ", p.MovedIds);
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
        private static string MoveOne(Document doc, Element elem, XYZ vector, DimensionHandling dims)
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
                        bool whole = dims == DimensionHandling.WholeThenText && TryMoveWhole(doc, dim, vector);
                        if (!whole && !MoveDimensionText(dim, vector))
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
