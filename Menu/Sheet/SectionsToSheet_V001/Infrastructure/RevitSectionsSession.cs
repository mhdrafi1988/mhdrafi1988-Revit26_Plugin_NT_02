using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Infrastructure
{
    /// <summary>
    /// <see cref="ISectionsSession"/> backed by an ExternalEvent. Must be created inside Revit's
    /// API context (the command): it reads the model and measures the sections straight away.
    /// </summary>
    public sealed class RevitSectionsSession : ISectionsSession
    {
        private readonly Document _doc;
        private readonly ElementId _sourceId;
        private readonly SectionsToSheetHandler _handler = new();
        private readonly ExternalEvent _externalEvent;

        /// <summary>Reads <paramref name="source"/>, resolves the saved title block / viewport type and measures.</summary>
        public RevitSectionsSession(UIDocument uidoc, View source, SectionsToSheetSettings settings)
        {
            if (uidoc == null) throw new ArgumentNullException(nameof(uidoc));
            _doc = uidoc.Document;
            _sourceId = source?.Id ?? throw new ArgumentNullException(nameof(source));
            settings ??= new SectionsToSheetSettings();

            Initial = SectionCollector.Read(_doc, source, uidoc.Selection.GetElementIds(), null);
            InitialTitleBlockKey = ResolveKey(Initial.TitleBlocks, settings.TitleBlockName) ?? Initial.TitleBlocks.FirstOrDefault()?.Key ?? 0;
            InitialViewportTypeKey = ResolveKey(Initial.ViewportTypes, settings.ViewportTypeName) ?? Initial.DefaultViewportTypeKey;
            InitialMeasure = FootprintMeasurer.Measure(_doc, InitialTitleBlockKey, InitialViewportTypeKey, Initial.Sections.Values);
            _externalEvent = ExternalEvent.Create(_handler);
        }

        /// <inheritdoc/>
        public SourceSnapshot Initial { get; }

        /// <inheritdoc/>
        public MeasureResult InitialMeasure { get; }

        /// <inheritdoc/>
        public long InitialTitleBlockKey { get; }

        /// <inheritdoc/>
        public long InitialViewportTypeKey { get; }

        /// <summary>Key of the type called <paramref name="name"/>, or null when there is none.</summary>
        public static long? ResolveKey(IReadOnlyList<NamedType> types, string name)
            => string.IsNullOrWhiteSpace(name) ? null
               : types?.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Key;

        /// <inheritdoc/>
        public bool Refresh(IReadOnlyCollection<long> keepKeys, Action<SessionResult> done) => Raise(done, app =>
        {
            var source = GetSource(out string error);
            return source == null ? Failed(error) : new SessionResult { Snapshot = Read(app, source, keepKeys) };
        });

        /// <inheritdoc/>
        public bool Pick(IReadOnlyCollection<long> keepKeys, Action<SessionResult> done) => Raise(done, app =>
        {
            var source = GetSource(out string error);
            if (source == null)
                return Failed(error);

            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || !uidoc.Document.Equals(_doc) || uidoc.ActiveView?.Id != _sourceId)
                return new SessionResult { Level = LogLevel.Warning, Message = $"Go back to the view '{source.Name}' to pick its section markers." };

            try
            {
                IList<Reference> picked = uidoc.Selection.PickObjects(ObjectType.Element, new MarkerPickFilter(),
                    "Pick section markers, then click Finish on the Options Bar (Esc cancels)");
                var keys = SectionCollector.ResolveSections(_doc, picked.Select(r => r.ElementId));
                return new SessionResult
                {
                    Snapshot = Read(app, source, (keepKeys ?? Array.Empty<long>()).Concat(keys).ToList()),
                    PickedKeys = keys,
                    Message = keys.Count == 0 ? "No section markers were picked." : $"Picked {keys.Count} section(s).",
                    Level = keys.Count == 0 ? LogLevel.Warning : LogLevel.Info
                };
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new SessionResult { Message = "Pick cancelled." };
            }
        });

        /// <inheritdoc/>
        public bool Measure(long titleBlockKey, long viewportTypeKey, IReadOnlyCollection<long> keys, Action<SessionResult> done) => Raise(done, app =>
        {
            var source = GetSource(out string error);
            if (source == null)
                return Failed(error);

            // Read the sections fresh, so locks and current sheets are up to date for the measuring.
            var snapshot = SectionCollector.Read(_doc, source, Array.Empty<ElementId>(), keys);
            var sections = (keys ?? Array.Empty<long>()).Where(snapshot.Sections.ContainsKey).Select(k => snapshot.Sections[k]);
            return new SessionResult { Measure = FootprintMeasurer.Measure(_doc, titleBlockKey, viewportTypeKey, sections) };
        });

        /// <inheritdoc/>
        public bool Apply(ApplyRequest request, Action<SessionResult> done) => Raise(done, app =>
        {
            var source = GetSource(out string error);
            if (source == null)
                return Failed(error);
            if (_doc.IsReadOnly)
                return Failed("The document is read-only. Nothing was changed.");

            var keep = request.TickedKeys.Concat(request.KeepKeys ?? Array.Empty<long>()).Distinct().ToList();
            var fresh = Read(app, source, keep);
            var ticked = request.TickedKeys.Where(fresh.Sections.ContainsKey).Select(k => fresh.Sections[k]).ToList();

            using var group = new TransactionGroup(_doc, PlacementRunner.TransactionName);
            if (group.Start() != TransactionStatus.Started)
                return Failed("Revit could not start the change. Nothing was changed.");

            try
            {
                var measure = FootprintMeasurer.Measure(_doc, request.TitleBlockKey, request.ViewportTypeKey, ticked);
                var plan = SectionPlanner.Build(new PlanRequest
                {
                    Sections = ticked,
                    Footprints = measure.Footprints,
                    MeasureProblems = measure.Problems,
                    TitleBlockFrame = measure.Frame,
                    FrameProblem = measure.FrameProblem,
                    Sheets = fresh.Sheets,
                    ExistingSheetNumbers = fresh.ExistingSheetNumbers,
                    SourceViewScale = fresh.SourceViewScale,
                    FirstSheetNumber = request.FirstSheetNumber,
                    SheetName = request.SheetName,
                    Settings = request.Settings
                });

                if (plan.Signature != request.Signature)
                {
                    group.RollBack();
                    return new SessionResult
                    {
                        Snapshot = fresh,
                        Measure = measure,
                        Level = LogLevel.Warning,
                        Message = "The model changed since the preview, so nothing was changed. Check the updated preview and press Create again."
                    };
                }

                if (!plan.CanApply)
                {
                    group.RollBack();
                    return new SessionResult { Snapshot = fresh, Measure = measure, Level = LogLevel.Warning, Message = "Nothing was changed: " + plan.Blocker };
                }

                var run = PlacementRunner.Apply(_doc, plan, request.TitleBlockKey, request.ViewportTypeKey,
                    request.Settings.DeleteEmptySheets, app.ActiveUIDocument?.ActiveView?.Id ?? ElementId.InvalidElementId);
                if (!run.Success)
                {
                    group.RollBack();
                    return new SessionResult { Snapshot = fresh, Measure = measure, Level = LogLevel.Error, Message = run.Error };
                }

                group.Assimilate();

                if (request.OpenSheet && run.CreatedSheets.Count > 0 && _doc.GetElement(run.CreatedSheets[0]) is View firstSheet)
                    app.ActiveUIDocument?.RequestViewChange(firstSheet);

                return new SessionResult
                {
                    Snapshot = Read(app, source, keep),
                    Measure = measure,
                    Applied = true,
                    LastSheetNumber = plan.Sheets.Last().Number,
                    Level = LogLevel.Success,
                    Message = $"Placed {run.Placed} section(s) on {string.Join(", ", plan.Sheets.Select(s => s.Number))}"
                              + (run.Removed > 0 ? $", {run.Removed} taken off their old sheet" : string.Empty)
                              + $". Undo with Ctrl+Z (\"{PlacementRunner.TransactionName}\").",
                    Details = Details(plan, run)
                };
            }
            finally
            {
                if (group.HasStarted() && !group.HasEnded())
                    group.RollBack();
            }
        });

        private static List<LogEntry> Details(SectionPlan plan, RunResult run)
        {
            var lines = new List<LogEntry>();
            foreach (var sheet in plan.Sheets)
                lines.Add(new LogEntry(LogLevel.Info, $"Sheet {sheet.Number} – {sheet.Name}: {sheet.Viewports.Count} view(s) in {sheet.RowCount} row(s)."));
            foreach (var move in plan.Moves)
                lines.Add(new LogEntry(LogLevel.Info, $"Moved {move.Section.Name}: {move.Section.CurrentSheetNumber} → {plan.Sheets[move.Sheet].Number} (detail {move.DetailNumber})."));
            foreach (var skip in plan.Skipped)
                lines.Add(new LogEntry(LogLevel.Warning, $"Skipped {skip.Section.Name}: {skip.Reason}."));
            foreach (var number in run.DeletedSheets)
                lines.Add(new LogEntry(LogLevel.Success, $"Deleted empty sheet {number}."));
            foreach (var kept in run.KeptSheets)
                lines.Add(new LogEntry(LogLevel.Warning, $"Kept empty sheet {kept}."));
            return lines;
        }

        private SourceSnapshot Read(UIApplication app, View source, IEnumerable<long> keepKeys)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            ICollection<ElementId> selection = uidoc != null && uidoc.Document.Equals(_doc)
                ? uidoc.Selection.GetElementIds()
                : Array.Empty<ElementId>();
            return SectionCollector.Read(_doc, source, selection, keepKeys);
        }

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
                    done(result ?? Failed("Unexpected error — see the error dialog. Nothing was changed."));
                }
            });
            return _externalEvent.Raise() == ExternalEventRequest.Accepted;
        }

        private View GetSource(out string error)
        {
            error = null;
            if (!_doc.IsValidObject)
                error = "The document was closed. Close this window.";
            else if (_doc.GetElement(_sourceId) is not View view)
                error = "The view the sections came from no longer exists. Close this window.";
            else
                return view;
            return null;
        }

        private static SessionResult Failed(string message) => new() { Level = LogLevel.Error, Message = message };

        /// <summary>Releases the ExternalEvent.</summary>
        public void Dispose() => _externalEvent.Dispose();

        /// <summary>Lets only section markers (and section views) be picked.</summary>
        private sealed class MarkerPickFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => SectionCollector.IsMarkerOrSection(elem);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
