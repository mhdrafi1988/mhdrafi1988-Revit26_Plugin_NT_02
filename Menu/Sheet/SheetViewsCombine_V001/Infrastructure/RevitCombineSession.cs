using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Infrastructure
{
    /// <summary>
    /// <see cref="ICombineSession"/> backed by an ExternalEvent. Must be created inside Revit's API
    /// context (the command): it reads the sheets list and the active sheet straight away.
    /// </summary>
    public sealed class RevitCombineSession : ICombineSession
    {
        private readonly Document _doc;
        private readonly SheetViewsCombineHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>Reads the sheets list and, when the active view is a sheet, its contents.</summary>
        public RevitCombineSession(UIDocument uidoc)
        {
            if (uidoc == null) throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;
            InitialCatalog = SheetReader.ReadCatalog(_doc, uidoc.ActiveView);
            InitialActiveSheet = InitialCatalog.ActiveSheetKey != 0 ? SheetReader.ReadSheet(_doc, InitialCatalog.ActiveSheetKey) : null;
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public ProjectCatalog InitialCatalog { get; }

        /// <inheritdoc/>
        public SheetContent InitialActiveSheet { get; }

        /// <inheritdoc/>
        public bool Refresh(IReadOnlyCollection<long> sheetKeys, Action<SessionResult> done) => Raise(done, app =>
        {
            if (CheckDocument() is string problem) return Failed(problem);
            return new SessionResult
            {
                Catalog = SheetReader.ReadCatalog(_doc, app.ActiveUIDocument?.ActiveView),
                Sheets = Read(sheetKeys)
            };
        });

        /// <inheritdoc/>
        public bool LoadSheets(IReadOnlyCollection<long> sheetKeys, Action<SessionResult> done) => Raise(done, _ =>
            CheckDocument() is string problem ? Failed(problem) : new SessionResult { Sheets = Read(sheetKeys) });

        /// <inheritdoc/>
        public bool MeasureTitleBlock(long titleBlockKey, Action<SessionResult> done) => Raise(done, _ =>
        {
            if (CheckDocument() is string problem) return Failed(problem);
            var (frame, why) = SheetReader.MeasureTitleBlock(_doc, titleBlockKey);
            return new SessionResult { MeasuredTitleBlockKey = titleBlockKey, TitleBlockFrame = frame, TitleBlockProblem = why };
        });

        /// <inheritdoc/>
        public bool OpenSheet(long sheetKey, Action<SessionResult> done) => Raise(done, app =>
        {
            if (CheckDocument() is string problem) return Failed(problem);
            if (_doc.GetElement(new ElementId(sheetKey)) is not ViewSheet sheet)
                return Failed("That sheet is no longer in the model.");
            app.ActiveUIDocument?.RequestViewChange(sheet);
            return new SessionResult { Message = $"Opened {sheet.SheetNumber}." };
        });

        /// <inheritdoc/>
        public bool Apply(ApplyRequest request, Action<SessionResult> done) => Raise(done, app =>
        {
            if (CheckDocument() is string problem) return Failed(problem);
            if (_doc.IsReadOnly) return Failed("The document is read-only. Nothing was changed.");

            var keys = new List<long> { request.Sheet1Key, request.Sheet2Key };
            if (request.Settings.Target == TargetMode.OtherSheet) keys.Add(request.OtherTargetKey);
            var fresh = Read(keys);
            var catalog = SheetReader.ReadCatalog(_doc, app.ActiveUIDocument?.ActiveView);

            using var group = new TransactionGroup(_doc, CombineRunner.TransactionName);
            if (group.Start() != TransactionStatus.Started)
                return Failed("Revit could not start the change. Nothing was changed.");
            try
            {
                (Core.Layout.SheetRect? Frame, string Problem) frame = (null, null);
                if (request.Settings.Target == TargetMode.NewSheet)
                    frame = SheetReader.MeasureTitleBlock(_doc, request.NewTitleBlockKey);

                var plan = CombinePlanner.Build(new CombineRequest
                {
                    Sheet1 = fresh.GetValueOrDefault(request.Sheet1Key),
                    Sheet2 = fresh.GetValueOrDefault(request.Sheet2Key),
                    OtherTarget = request.Settings.Target == TargetMode.OtherSheet ? fresh.GetValueOrDefault(request.OtherTargetKey) : null,
                    Ticked = request.Ticked,
                    Settings = request.Settings,
                    NewSheetNumber = request.NewSheetNumber,
                    NewSheetName = request.NewSheetName,
                    NewTitleBlockKey = request.NewTitleBlockKey,
                    NewTitleBlockFrame = frame.Frame,
                    NewTitleBlockProblem = frame.Problem,
                    ExistingSheetNumbers = catalog.ExistingSheetNumbers
                });

                if (plan.Signature != request.Signature)
                {
                    group.RollBack();
                    return new SessionResult
                    {
                        Catalog = catalog,
                        Sheets = fresh,
                        Level = LogLevel.Warning,
                        Message = "The sheets changed since the preview, so nothing was changed. Check the updated preview and press Move Views again."
                    };
                }
                if (!plan.CanApply)
                {
                    group.RollBack();
                    return new SessionResult { Catalog = catalog, Sheets = fresh, Level = LogLevel.Warning, Message = "Nothing was changed: " + plan.Blocker };
                }

                var run = CombineRunner.Apply(_doc, plan, request.Settings);
                if (!run.Success)
                {
                    group.RollBack();
                    return new SessionResult { Catalog = catalog, Sheets = fresh, Run = run, Level = LogLevel.Error, Message = run.Error };
                }
                group.Assimilate();

                int done = run.Results.Count(r => r.Outcome is ViewOutcome.Moved or ViewOutcome.Repacked or ViewOutcome.Copied);
                int skipped = run.Results.Count(r => r.Outcome == ViewOutcome.Skipped);
                var details = run.Results.Select(r => new LogEntry(
                    r.Outcome == ViewOutcome.Skipped ? LogLevel.Warning : LogLevel.Success,
                    $"{OutcomeWord(r.Outcome)} {r.Name}: {r.Detail}.")).ToList();
                details.AddRange(run.CreatedSheets.Select(n => new LogEntry(LogLevel.Info, $"Created sheet {n}.")));
                details.AddRange(run.Notes.Select(n => new LogEntry(LogLevel.Warning, n)));

                // Sheets read after the run, so the grid shows where everything is now.
                var after = Read(keys.Concat(new[] { run.TargetSheetKey }).Where(k => k != 0).Distinct().ToList());
                return new SessionResult
                {
                    Catalog = SheetReader.ReadCatalog(_doc, app.ActiveUIDocument?.ActiveView),
                    Sheets = after,
                    Run = run,
                    Applied = true,
                    Level = skipped > 0 ? LogLevel.Warning : LogLevel.Success,
                    Message = $"Done: {done} of {done + skipped} view(s)" + (skipped > 0 ? $", {skipped} skipped" : string.Empty)
                              + $". Undo with Ctrl+Z (\"{CombineRunner.TransactionName}\").",
                    Details = details
                };
            }
            finally
            {
                if (group.HasStarted() && !group.HasEnded())
                    group.RollBack();
            }
        });

        private static string OutcomeWord(ViewOutcome outcome) => outcome switch
        {
            ViewOutcome.Moved => "Moved",
            ViewOutcome.Repacked => "Re-packed",
            ViewOutcome.Copied => "Copied",
            ViewOutcome.Failed => "Failed",
            _ => "Skipped"
        };

        private Dictionary<long, SheetContent> Read(IEnumerable<long> keys)
        {
            var result = new Dictionary<long, SheetContent>();
            foreach (long key in (keys ?? Array.Empty<long>()).Where(k => k != 0).Distinct())
                result[key] = SheetReader.ReadSheet(_doc, key);
            return result;
        }

        private string CheckDocument() => _doc.IsValidObject ? null : "The document was closed. Close this window.";

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
                    done(result ?? Failed(ToolCancellation.CancelledOr("Unexpected error — see the error dialog and the log folder. Nothing was changed.")));
                }
            });
            // Pending: the event is already raised and will run this request too.
            var raised = _externalEvent.Raise();
            return raised == ExternalEventRequest.Accepted || raised == ExternalEventRequest.Pending;
        }

        private static SessionResult Failed(string message) => new() { Level = LogLevel.Error, Message = message };

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();
    }
}
