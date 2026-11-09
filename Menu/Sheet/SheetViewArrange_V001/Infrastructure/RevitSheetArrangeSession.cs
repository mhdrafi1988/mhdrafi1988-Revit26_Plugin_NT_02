using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Infrastructure
{
    /// <summary>
    /// <see cref="ISheetArrangeSession"/> backed by an ExternalEvent. Must be created inside
    /// Revit's API context (the command).
    /// </summary>
    public sealed class RevitSheetArrangeSession : ISheetArrangeSession
    {
        private readonly Document _doc;
        private readonly ElementId _sheetId;
        private readonly SheetViewArrangeHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>Reads <paramref name="sheet"/> and prepares the ExternalEvent.</summary>
        public RevitSheetArrangeSession(Document doc, ViewSheet sheet)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _sheetId = sheet?.Id ?? throw new ArgumentNullException(nameof(sheet));
            Initial = SheetReader.Read(doc, sheet);
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public SheetSnapshot Initial { get; }

        /// <inheritdoc/>
        public bool Refresh(Action<SessionResult> done) => Raise(done, () =>
        {
            var sheet = GetSheet(out string error);
            return sheet == null
                ? Failed(error)
                : new SessionResult { Snapshot = SheetReader.Read(_doc, sheet) };
        });

        /// <inheritdoc/>
        public bool Apply(SheetViewArrangeSettings settings, IReadOnlySet<long> unticked, string previewSignature, Action<SessionResult> done) => Raise(done, () =>
        {
            var sheet = GetSheet(out string error);
            if (sheet == null)
                return Failed(error);

            var fresh = SheetReader.Read(_doc, sheet);
            var plan = ArrangePlanner.Build(fresh, settings, unticked);

            if (plan.Signature != previewSignature)
                return new SessionResult
                {
                    Snapshot = fresh,
                    Level = LogLevel.Warning,
                    Message = "The sheet changed since the preview — nothing was moved. Check the updated preview and press Apply again."
                };

            if (!plan.CanApply)
                return new SessionResult
                {
                    Snapshot = fresh,
                    Level = LogLevel.Warning,
                    Message = "Nothing moved: " + (plan.Blocker ?? "every view is already in place.")
                };

            var result = ArrangeApplier.Apply(_doc, plan);
            return new SessionResult
            {
                Snapshot = SheetReader.Read(_doc, sheet),
                Level = result.Success ? LogLevel.Success : LogLevel.Error,
                Message = result.Success
                    ? $"Arranged {result.Moved} view(s) in {plan.RowCount} row(s). Undo with Ctrl+Z (\"{ArrangeApplier.TransactionName}\")."
                    : result.Error
            };
        });

        /// <summary>
        /// Queues <paramref name="work"/>; <paramref name="done"/> always runs afterwards, also when
        /// the work throws (the exception then goes on to ToolGuard, which logs and shows it).
        /// </summary>
        private bool Raise(Action<SessionResult> done, Func<SessionResult> work)
        {
            _handler.Queue(_ =>
            {
                SessionResult result = null;
                try
                {
                    result = work();
                }
                finally
                {
                    done(result ?? Failed(ToolCancellation.CancelledOr("Unexpected error — see the error dialog. Nothing was moved.", "Cancelled by user. Nothing was moved.")));
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
