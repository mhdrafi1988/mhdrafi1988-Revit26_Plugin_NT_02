using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services
{
    /// <summary>What happened to one view in a run.</summary>
    public sealed class ViewResult
    {
        /// <summary>Viewport key as read before the run.</summary>
        public long ViewportKey { get; init; }

        /// <summary>View name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>1 = Sheet 1, 2 = Sheet 2, 3 = other target.</summary>
        public int Slot { get; init; }

        /// <summary>Outcome.</summary>
        public ViewOutcome Outcome { get; init; }

        /// <summary>Where it went, or why it was skipped.</summary>
        public string Detail { get; init; } = string.Empty;
    }

    /// <summary>Outcome of <see cref="CombineRunner.Apply"/>.</summary>
    public sealed class CombineRunResult
    {
        /// <summary>True when the transaction committed.</summary>
        public bool Success { get; init; }

        /// <summary>Why the run failed (nothing was changed), when <see cref="Success"/> is false.</summary>
        public string Error { get; init; }

        /// <summary>One line per view the run touched or skipped.</summary>
        public List<ViewResult> Results { get; init; } = new();

        /// <summary>Sheets created by the run, in order.</summary>
        public List<string> CreatedSheets { get; init; } = new();

        /// <summary>The target sheet after the run (0 when it was not created).</summary>
        public long TargetSheetKey { get; init; }

        /// <summary>Notes for the log (detail numbers left as Revit set them …).</summary>
        public List<string> Notes { get; init; } = new();
    }

    /// <summary>
    /// Carries out a <see cref="CombinePlan"/> in one transaction. With
    /// <see cref="FailureMode.SkipAndReport"/> each view runs in its own sub-transaction, so a
    /// failure undoes only that view and the run goes on. With <see cref="FailureMode.StopAndUndo"/>
    /// the first failure ends the run and everything is rolled back.
    /// </summary>
    public static class CombineRunner
    {
        /// <summary>Name of the step in Revit's Undo list.</summary>
        public const string TransactionName = "Combine Sheet Views";

        private sealed class StopException : Exception
        {
            public StopException(string message) : base(message) { }
        }

        /// <summary>Applies <paramref name="plan"/> (the caller has checked <see cref="CombinePlan.CanApply"/>).</summary>
        public static CombineRunResult Apply(Document doc, CombinePlan plan, SheetViewsCombineSettings settings)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            settings ??= new SheetViewsCombineSettings();
            if (!plan.CanApply)
                return new CombineRunResult { Error = plan.Blocker ?? "Nothing to do." };
            if (doc.IsReadOnly)
                return new CombineRunResult { Error = "The document is read-only." };

            bool stop = settings.OnFailure == FailureMode.StopAndUndo;
            var results = new List<ViewResult>();
            var notes = new List<string>();
            var created = new List<string>();
            var sheetIds = new Dictionary<int, ElementId>();
            var placedVps = new Dictionary<PlannedView, Viewport>();

            // Skipped by the plan: reported, never touched.
            foreach (var item in plan.Items.Where(i => i.Action == ViewAction.Skip))
                results.Add(Result(item, ViewOutcome.Skipped, item.Reason ?? "it cannot be placed"));

            using var tx = new Transaction(doc, TransactionName);
            var failures = QuietFailures.Attach(tx);
            try
            {
                if (tx.Start() != TransactionStatus.Started)
                    return new CombineRunResult { Error = "Revit could not start the change. Nothing was changed.", Results = results };

                int total = plan.Items.Count(i => i.Changes), done = 0;
                foreach (var sheet in plan.Sheets)
                {
                    ToolCancellation.CheckCurrent();
                    string sheetError = Step(doc, stop, $"sheet {sheet.Number}", () =>
                    {
                        if (!sheet.IsNew)
                        {
                            if (doc.GetElement(new ElementId(sheet.ExistingKey)) is not ViewSheet existing)
                                throw new InvalidOperationException($"sheet {sheet.Number} is no longer in the model");
                            sheetIds[sheet.Index] = existing.Id;
                            return;
                        }
                        if (doc.GetElement(new ElementId(sheet.TitleBlockKey)) is not FamilySymbol titleBlock)
                            throw new InvalidOperationException("its title block is no longer in the project");
                        if (!titleBlock.IsActive)
                            titleBlock.Activate();
                        var vs = ViewSheet.Create(doc, titleBlock.Id);
                        vs.SheetNumber = sheet.Number;
                        vs.Name = sheet.Name;
                        sheetIds[sheet.Index] = vs.Id;
                        created.Add(sheet.Number);
                    });

                    var items = sheet.Views.Where(v => v.Changes).ToList();
                    if (sheetError != null)
                    {
                        foreach (var item in items)
                            results.Add(Result(item, ViewOutcome.Skipped, $"sheet {sheet.Number} could not be created: {sheetError}"));
                        continue;
                    }

                    var sheetId = sheetIds[sheet.Index];
                    foreach (var item in items)
                    {
                        ToolCancellation.CheckCurrent();
                        ToolCancellation.ReportCurrent(done++, total, $"{item.View.Name} → {sheet.Number}");
                        Viewport placed = null;
                        string error = Step(doc, stop, item.View.Name, () => placed = Place(doc, item, sheetId, settings));
                        if (error != null)
                        {
                            results.Add(Result(item, ViewOutcome.Skipped, error + $". It stays on {item.View.SheetNumber}"));
                            continue;
                        }
                        placedVps[item] = placed;
                        results.Add(Result(item, Outcome(item.Action), Where(item, sheet)));
                    }
                }

                // Detail numbers, per sheet: temporary unique numbers first so swaps never clash.
                foreach (var group in placedVps.GroupBy(p => p.Key.SheetIndex))
                {
                    string sheetNumber = plan.Sheets[group.Key].Number;
                    string error = Step(doc, stop, $"detail numbers on {sheetNumber}", () => SetDetailNumbers(group.ToList()));
                    if (error != null)
                        notes.Add($"Detail numbers on {sheetNumber} were left as Revit set them: {error}.");
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return new CombineRunResult
                    {
                        Error = "Revit refused the change, so nothing was changed" +
                                (failures.Errors.Count > 0 ? ": " + string.Join(" ", failures.Errors.Distinct()) : "."),
                        Results = results
                    };

                long targetKey = sheetIds.TryGetValue(0, out var targetId) ? targetId.Value : 0;
                return new CombineRunResult { Success = true, Results = results, CreatedSheets = created, TargetSheetKey = targetKey, Notes = notes };
            }
            catch (StopException ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return new CombineRunResult { Error = ex.Message + " The run stopped and nothing was changed.", Results = results };
            }
            finally
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
            }
        }

        /// <summary>
        /// Runs <paramref name="work"/> in a sub-transaction. Returns null on success, or the reason
        /// it failed (its changes rolled back). With <paramref name="stop"/>, a failure throws instead.
        /// </summary>
        private static string Step(Document doc, bool stop, string what, Action work)
        {
            using var sub = new SubTransaction(doc);
            try
            {
                sub.Start();
                work();
                doc.Regenerate();
                sub.Commit();
                return null;
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                if (sub.HasStarted() && !sub.HasEnded())
                    sub.RollBack();
                string reason = Clean(ex.Message);
                if (stop)
                    throw new StopException($"'{what}' failed: {reason}.");
                return reason;
            }
        }

        private static Viewport Place(Document doc, PlannedView item, ElementId sheetId, SheetViewsCombineSettings settings)
        {
            var centre = new XYZ(item.CenterX, item.CenterY, 0);
            if (doc.GetElement(new ElementId(item.View.ViewportKey)) is not Viewport old)
                throw new InvalidOperationException("its viewport is no longer in the model");
            if (SheetReader.LockReason(doc, old) is string lockReason)
                throw new InvalidOperationException(lockReason);

            if (item.Action == ViewAction.Repack)
            {
                bool pinned = old.Pinned;
                if (pinned) old.Pinned = false;
                old.SetBoxCenter(centre);
                if (pinned) old.Pinned = true;
                return old;
            }

            // Remember what the old viewport looked like.
            ElementId viewId = old.ViewId;
            ElementId typeId = old.GetTypeId();
            ViewportRotation rotation = old.Rotation;
            XYZ labelOffset = TryGet(() => old.LabelOffset);
            double? labelLength = TryGet<double?>(() => old.LabelLineLength);
            bool wasPinned = old.Pinned;

            if (item.Action == ViewAction.Move)
            {
                if (wasPinned) old.Pinned = false;
                doc.Delete(old.Id); // a view can be on one sheet only
                doc.Regenerate();
            }

            if (!Viewport.CanAddViewToSheet(doc, sheetId, viewId))
                throw new InvalidOperationException("Revit cannot put this view on the target sheet");
            var vp = Viewport.Create(doc, sheetId, viewId, centre)
                     ?? throw new InvalidOperationException("Revit did not create a viewport");

            if (settings.KeepViewportType && typeId != ElementId.InvalidElementId && vp.GetTypeId() != typeId && vp.IsValidType(typeId))
                vp.ChangeTypeId(typeId);
            if (rotation != ViewportRotation.None && vp.Rotation != rotation)
                vp.Rotation = rotation;
            if (labelOffset != null)
                TryDo(() => vp.LabelOffset = labelOffset);
            if (labelLength is double length && length > 0)
                TryDo(() => vp.LabelLineLength = length);
            doc.Regenerate();
            vp.SetBoxCenter(centre); // rotation and type can shift the box; put it back on the planned spot
            if (wasPinned) vp.Pinned = true;
            return vp;
        }

        private static void SetDetailNumbers(List<KeyValuePair<PlannedView, Viewport>> placed)
        {
            var toChange = placed
                .Where(p => !string.IsNullOrWhiteSpace(p.Key.DetailNumber))
                .Where(p => p.Value.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() != p.Key.DetailNumber)
                .ToList();
            if (toChange.Count == 0)
                return;
            foreach (var (_, vp) in toChange)
                vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.Set("~" + vp.Id.Value.ToString(CultureInfo.InvariantCulture));
            foreach (var (item, vp) in toChange)
                vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.Set(item.DetailNumber);
        }

        private static ViewOutcome Outcome(ViewAction action) => action switch
        {
            ViewAction.Repack => ViewOutcome.Repacked,
            ViewAction.CopyLegend => ViewOutcome.Copied,
            _ => ViewOutcome.Moved
        };

        private static string Where(PlannedView item, PlannedSheet sheet) => item.Action switch
        {
            ViewAction.Repack => $"{sheet.Number}, detail {item.DetailNumber}",
            ViewAction.CopyLegend => $"{sheet.Number}, same spot as on {item.View.SheetNumber}, detail {item.DetailNumber}",
            _ => $"{item.View.SheetNumber} → {sheet.Number}, detail {item.DetailNumber}"
        };

        private static ViewResult Result(PlannedView item, ViewOutcome outcome, string detail) => new()
        {
            ViewportKey = item.View.ViewportKey,
            Name = item.View.Name,
            Slot = item.Slot,
            Outcome = outcome,
            Detail = detail
        };

        private static string Clean(string message)
            => string.IsNullOrWhiteSpace(message) ? "Revit refused the change" : message.Trim().TrimEnd('.');

        private static T TryGet<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException) { return default; }
        }

        private static void TryDo(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is ArgumentException || ex is InvalidOperationException)
            {
                // Cosmetic (title position): keep Revit's default.
            }
        }
    }
}
