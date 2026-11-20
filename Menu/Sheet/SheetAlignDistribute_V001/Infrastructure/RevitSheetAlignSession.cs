using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Infrastructure
{
    /// <summary>
    /// <see cref="ISheetAlignSession"/> backed by an ExternalEvent. Must be created inside Revit's
    /// API context (the command), and only ever looks at the one sheet it was opened on.
    /// </summary>
    public sealed class RevitSheetAlignSession : ISheetAlignSession
    {
        private readonly Document _doc;
        private readonly ElementId _sheetId;
        private readonly SheetAlignDistributeHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>Reads <paramref name="sheet"/> and prepares the ExternalEvent.</summary>
        public RevitSheetAlignSession(Document doc, ViewSheet sheet, FootprintMode footprintMode)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _sheetId = sheet?.Id ?? throw new ArgumentNullException(nameof(sheet));
            Initial = SheetElementReader.Read(doc, sheet, footprintMode);
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public SheetSnapshot Initial { get; }

        /// <inheritdoc/>
        public bool Refresh(FootprintMode footprintMode, Action<SessionResult> done) => Raise(done, _ =>
        {
            var sheet = GetSheet(out string error);
            return sheet == null
                ? Failed(error)
                : new SessionResult { Snapshot = SheetElementReader.Read(_doc, sheet, footprintMode) };
        });

        /// <summary>How far an element may sit from where the preview found it and still count as unmoved (~0.5 mm).</summary>
        private const double StaleTolerance = 0.5 / 304.8;

        /// <inheritdoc/>
        public bool Apply(AlignPlan plan, FootprintMode footprintMode, Action<SessionResult> done) => Raise(done, app =>
        {
            var sheet = GetSheet(out string error);
            if (sheet == null)
                return Failed(error);
            if (plan == null || !plan.CanApply)
                return new SessionResult
                {
                    Level = LogLevel.Warning,
                    Snapshot = SheetElementReader.Read(_doc, sheet, footprintMode),
                    Message = "Nothing moved: " + (plan?.Blocker ?? "every element is already in place.")
                };

            // Check the preview is still true rather than re-planning: a re-plan can differ from the
            // preview for reasons that are not a change on the sheet, and then nothing would ever move.
            // Someone may still have moved, deleted or taken ownership of a view while this modeless
            // window was open, so every element to be moved must be where the preview found it.
            var fresh = SheetElementReader.Read(_doc, sheet, footprintMode);
            var now = fresh.Elements.ToDictionary(e => e.Key);
            var stale = new List<string>();
            foreach (var move in plan.Moves.Where(m => m.NeedsMove))
            {
                if (!now.TryGetValue(move.Key, out var current))
                    stale.Add($"'{move.Element.Name}' is no longer on the sheet");
                else if (current.LockReason != null)
                    stale.Add($"'{move.Element.Name}': {current.LockReason}");
                else if (!SamePlace(current.Footprint, move.Element.Footprint))
                    stale.Add($"'{move.Element.Name}' has moved since the preview");
            }

            if (stale.Count > 0)
                return new SessionResult
                {
                    Snapshot = fresh,
                    Level = LogLevel.Warning,
                    Message = "Nothing was moved: " + string.Join("; ", stale.Take(3))
                              + (stale.Count > 3 ? $" (and {stale.Count - 3} more)" : "")
                              + ". Check the updated preview and press Apply again."
                };

            var result = AlignApplier.Apply(_doc, plan);
            if (!result.Success)
                return new SessionResult
                {
                    Snapshot = SheetElementReader.Read(_doc, sheet, footprintMode),
                    Level = LogLevel.Error,
                    Message = result.Error
                };

            try { app?.ActiveUIDocument?.RefreshActiveView(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* the redraw follows on its own */ }

            // Read back what Revit now has, so a commit that did not really move anything is reported.
            var after = SheetElementReader.Read(_doc, sheet, footprintMode);
            var afterByKey = after.Elements.ToDictionary(e => e.Key);
            int notMoved = plan.Moves.Where(m => m.NeedsMove).Count(m =>
                !afterByKey.TryGetValue(m.Key, out var placed)
                || !SamePlace(placed.Footprint, m.Element.Footprint.Offset(m.DeltaX, m.DeltaY)));

            return new SessionResult
            {
                Snapshot = after,
                Level = notMoved == 0 ? LogLevel.Success : LogLevel.Warning,
                Moved = result.Moved - notMoved,
                Applied = true,
                Message = notMoved == 0
                    ? $"Aligned {result.Moved} element(s) in {plan.RowCount} row(s) and {plan.ColumnCount} column(s). " +
                      $"Undo with Ctrl+Z (\"{AlignApplier.TransactionName}\")."
                    : $"Committed, but {notMoved} of {result.Moved} element(s) are not where the plan put them — " +
                      "check the sheet, and Undo with Ctrl+Z if it is wrong."
            };
        });

        private static bool SamePlace(LayoutRect a, LayoutRect b) =>
            Math.Abs(a.MinX - b.MinX) <= StaleTolerance && Math.Abs(a.MinY - b.MinY) <= StaleTolerance &&
            Math.Abs(a.MaxX - b.MaxX) <= StaleTolerance && Math.Abs(a.MaxY - b.MaxY) <= StaleTolerance;

        /// <summary>
        /// Queues <paramref name="work"/>; <paramref name="done"/> always runs afterwards, also when
        /// the work throws (the exception then goes on to ToolGuard, which logs and shows it).
        /// </summary>
        private bool Raise(Action<SessionResult> done, Func<UIApplication, SessionResult> work)
        {
            _handler.Queue(app =>
            {
                SessionResult result = null;
                try
                {
                    result = work(app);
                }
                finally
                {
                    done(result ?? Failed(ToolCancellation.CancelledOr(
                        "Unexpected error — see the error dialog. Nothing was moved.",
                        "Cancelled by user. Nothing was moved.")));
                }
            });
            return _externalEvent.Raise() == ExternalEventRequest.Accepted;
        }

        private ViewSheet GetSheet(out string error)
        {
            error = null;
            if (!_doc.IsValidObject)
                error = "The document was closed. Close this window.";
            else if (_doc.GetElement(_sheetId) is not ViewSheet sheet)
                error = "The sheet no longer exists. Close this window.";
            else
                return sheet;
            return null;
        }

        private static SessionResult Failed(string message)
            => new() { Level = LogLevel.Error, Message = message };

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();
    }
}
