using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TagToText.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TagToText.V001.Core.Services
{
    /// <summary>One tag to copy: what was read, plus the text lines to write (the user may have edited them).</summary>
    public sealed class TagJob
    {
        /// <summary>The tag as read.</summary>
        public TagSnapshot Snapshot { get; init; }

        /// <summary>The lines to write.</summary>
        public string[] Lines { get; init; }
    }

    /// <summary>Everything one run needs.</summary>
    public sealed class TagToTextRequest
    {
        /// <summary>The view the tags are in.</summary>
        public ElementId SourceViewId { get; init; }

        /// <summary>The tags to copy.</summary>
        public IReadOnlyList<TagJob> Jobs { get; init; }

        /// <summary>Text note type for the new text.</summary>
        public ElementId TextTypeId { get; init; }

        /// <summary>Options from the window.</summary>
        public TagToTextSettings Options { get; init; }

        /// <summary>Name wanted for the new view (made unique if taken).</summary>
        public string NewViewName { get; init; }
    }

    /// <summary>Progress of a run, reported after each tag.</summary>
    public sealed class TagToTextProgress
    {
        /// <summary>Tags handled so far.</summary>
        public int Done { get; init; }

        /// <summary>Tags in the run.</summary>
        public int Total { get; init; }

        /// <summary>What the run is doing now.</summary>
        public string Step { get; init; }

        /// <summary>Tags turned into text.</summary>
        public int Created { get; init; }

        /// <summary>Tags skipped.</summary>
        public int Skipped { get; init; }

        /// <summary>Tags that failed.</summary>
        public int Failed { get; init; }
    }

    /// <summary>Outcome of a run.</summary>
    public sealed class TagToTextResult
    {
        /// <summary>Tags turned into text.</summary>
        public int Created { get; set; }

        /// <summary>Text notes made (a Spot Coordinate makes two).</summary>
        public int Notes { get; set; }

        /// <summary>Tags skipped.</summary>
        public int Skipped { get; set; }

        /// <summary>Tags that failed.</summary>
        public int Failed { get; set; }

        /// <summary>Original tags hidden.</summary>
        public int Hidden { get; set; }

        /// <summary>Original tags deleted.</summary>
        public int Deleted { get; set; }

        /// <summary>True when the user cancelled; nothing was kept.</summary>
        public bool Cancelled { get; set; }

        /// <summary>The new view, when one was made and kept.</summary>
        public View NewView { get; set; }

        /// <summary>Reasons the new text may not show in its view (hidden category, filter, workset, crop…).</summary>
        public List<string> VisibilityWarnings { get; } = new List<string>();

        /// <inheritdoc/>
        public override string ToString()
        {
            if (Cancelled) return "Cancelled. Nothing was changed.";
            string s = $"Created {Notes} text note(s) from {Created} tag(s); skipped {Skipped}; failed {Failed}.";
            if (Hidden > 0) s += $" Hid {Hidden} tag(s).";
            if (Deleted > 0) s += $" Deleted {Deleted} tag(s).";
            if (NewView != null) s += $" New view: {NewView.Name}.";
            if (VisibilityWarnings.Count > 0) s += $" {VisibilityWarnings.Count} visibility warning(s): the new text may not show.";
            return s;
        }
    }

    /// <summary>
    /// Runs Tag To Text inside one <see cref="TransactionGroup"/>: optional view duplicate, one text
    /// note (two for a Spot Coordinate) per tag, then hide or delete of the originals. Any error or a
    /// cancel rolls the whole group back. Must be called in a Revit API context.
    /// </summary>
    public sealed class TagToTextService
    {
        private readonly Action<LogEntry> _log;
        private readonly Action<TagToTextProgress> _progress;
        private readonly Func<bool> _isCancelled;

        /// <summary>Creates the service.</summary>
        /// <param name="log">Receives log lines.</param>
        /// <param name="progress">Receives progress after each tag.</param>
        /// <param name="isCancelled">Polled after each tag; true stops the run and rolls back.</param>
        public TagToTextService(Action<LogEntry> log, Action<TagToTextProgress> progress, Func<bool> isCancelled)
        {
            _log = log ?? (_ => { });
            _progress = progress ?? (_ => { });
            _isCancelled = isCancelled ?? (() => false);
        }

        /// <summary>Runs <paramref name="request"/> on <paramref name="doc"/>.</summary>
        public TagToTextResult Run(Document doc, TagToTextRequest request)
        {
            var result = new TagToTextResult();
            var options = request.Options;
            if (doc.GetElement(request.SourceViewId) is not View sourceView)
                throw new InvalidOperationException("The view the tags were read from no longer exists.");

            int total = request.Jobs.Count;
            using var group = new TransactionGroup(doc, "Tag To Text");
            group.Start();

            View target = sourceView;
            if (options.Target == TargetMode.NewView)
            {
                Report(0, total, "Duplicating the view…", result);
                target = DuplicateView(doc, sourceView, options.Duplicate, request.NewViewName);
                result.NewView = target;
                _log(new LogEntry(LogLevel.Info, $"Duplicated '{sourceView.Name}' as '{target.Name}' ({DuplicateLabel(options.Duplicate)})."));
            }

            bool commentsMissingLogged = false;
            var converted = new List<TagSnapshot>();
            var noteIds = new List<ElementId>();

            using (var t = new Transaction(doc, "Tag To Text: create text"))
            {
                t.Start();
                double lineStep = LineStep(doc, target, request.TextTypeId);
                string date = DateTime.Now.ToString("yyyy-MM-dd");

                for (int i = 0; i < total; i++)
                {
                    TagJob job = request.Jobs[i];
                    TagSnapshot s = job.Snapshot;
                    Report(i, total, $"Creating text notes… ({i + 1} of {total})", result);

                    if (_isCancelled()) { result.Cancelled = true; break; }

                    if (job.Lines == null || job.Lines.Length == 0 || s.TextPoint == null)
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Warning, $"Skipped tag {s.TagId.Value}: no text to copy."));
                        continue;
                    }

                    using var st = new SubTransaction(doc);
                    try
                    {
                        st.Start();
                        string[] lines = TextRules.ApplyUserAffixes(job.Lines, options.Prefix, options.Suffix);
                        int made = CreateNotes(doc, target, request.TextTypeId, s, lines, options, lineStep,
                            options.AddSourceNote ? $"From tag {s.TagId.Value} · {date}" : null,
                            noteIds, ref commentsMissingLogged);
                        st.Commit();
                        result.Created++;
                        result.Notes += made;
                        converted.Add(s);
                    }
                    catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or ArgumentException or InvalidOperationException)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        result.Failed++;
                        _log(new LogEntry(LogLevel.Error, $"Tag {s.TagId.Value} ({s.TypeLabel}): {ex.Message}"));
                    }
                }

                if (!result.Cancelled && options.Target == TargetMode.SameView && converted.Count > 0)
                    HandleOriginals(doc, sourceView, converted, options.OriginalAction, result);

                if (!result.Cancelled && noteIds.Count > 0)
                {
                    result.VisibilityWarnings.AddRange(TextVisibilityChecker.Check(doc, target, noteIds));
                    foreach (string w in result.VisibilityWarnings)
                        _log(new LogEntry(LogLevel.Warning, "Not visible: " + w));
                }

                if (result.Cancelled) t.RollBack();
                else t.Commit();
            }

            if (result.Cancelled)
            {
                group.RollBack();
                result.NewView = null;
                _log(new LogEntry(LogLevel.Warning, "Cancelled. Everything this run did was rolled back."));
            }
            else
            {
                group.Assimilate();
            }

            Report(total, total, result.Cancelled ? "Cancelled" : "Done", result);
            return result;
        }

        private void Report(int done, int total, string step, TagToTextResult r) =>
            _progress(new TagToTextProgress
            {
                Done = done, Total = total, Step = step, Created = r.Created, Skipped = r.Skipped, Failed = r.Failed
            });

        private static string DuplicateLabel(DuplicateMode mode) =>
            mode == DuplicateMode.WithDetailing ? "with detailing" : "without detailing";

        private static View DuplicateView(Document doc, View source, DuplicateMode mode, string wantedName)
        {
            var option = mode == DuplicateMode.WithDetailing ? ViewDuplicateOption.WithDetailing : ViewDuplicateOption.Duplicate;
            if (!source.CanViewBeDuplicated(option))
                throw new InvalidOperationException($"Revit can't duplicate '{source.Name}' {DuplicateLabel(mode)}.");

            using var t = new Transaction(doc, "Tag To Text: duplicate view");
            t.Start();
            var view = (View)doc.GetElement(source.Duplicate(option));
            var names = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.Id != view.Id).Select(v => v.Name);
            view.Name = TextRules.UniqueViewName(wantedName, names);
            t.Commit();
            return view;
        }

        private int CreateNotes(Document doc, View view, ElementId typeId, TagSnapshot s, string[] lines,
            TagToTextSettings options, double lineStep, string sourceNote, List<ElementId> noteIds, ref bool commentsMissingLogged)
        {
            // A Spot Coordinate becomes two notes (N, then E below it); everything else one note.
            var texts = s.Kind == TagKind.SpotCoordinate && lines.Length >= 2
                ? lines.Select(l => new[] { l }).ToList()
                : new List<string[]> { lines };

            bool leader = options.KeepLeader && s.HasLeader;
            for (int i = 0; i < texts.Count; i++)
            {
                XYZ point = s.TextPoint - view.UpDirection * (lineStep * i);
                var noteOptions = new TextNoteOptions(typeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    VerticalAlignment = VerticalTextAlignment.Middle,
                    Rotation = options.MatchRotation ? s.Rotation : 0,
                    KeepRotatedTextReadable = true
                };
                TextNote note = TextNote.Create(doc, view.Id, point, string.Join("\r", texts[i]), noteOptions);
                noteIds.Add(note.Id);

                if (leader && s.LeaderEnd.DistanceTo(point) > 1e-6)
                {
                    try
                    {
                        AddLeader(doc, view, note, s, point, firstNote: i == 0);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        _log(new LogEntry(LogLevel.Warning, $"Tag {s.TagId.Value}: text created without a leader ({ex.Message})."));
                    }
                }

                if (sourceNote != null)
                {
                    Parameter comments = note.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (comments != null && !comments.IsReadOnly) comments.Set(sourceNote);
                    else if (!commentsMissingLogged)
                    {
                        commentsMissingLogged = true;
                        _log(new LogEntry(LogLevel.Warning, "Text notes here have no editable Comments parameter, so the source note was not added."));
                    }
                }
            }
            return texts.Count;
        }

        // Adds a leader that follows the tag's: arrow at the tagged point, bend where the tag bends, and
        // (for the first note) starting where the tag's leader leaves its text. Revit decides where a
        // note's leader starts (its "anchor", on the side of the text), so the note is created, the anchor
        // is read back, and the note is moved by the difference. A tag leader without a bend gets its bend
        // on the straight line, so Revit doesn't add its own kink.
        private static void AddLeader(Document doc, View view, TextNote note, TagSnapshot s, XYZ textPoint, bool firstNote)
        {
            XYZ towards = (firstNote ? s.LeaderStart ?? s.LeaderElbow : s.LeaderElbow) ?? s.LeaderEnd;
            bool leaderOnLeft = (towards - textPoint).DotProduct(view.RightDirection) < 0;
            note.AddLeader(leaderOnLeft ? TextNoteLeaderTypes.TNLT_STRAIGHT_L : TextNoteLeaderTypes.TNLT_STRAIGHT_R);
            doc.Regenerate();

            if (firstNote && s.LeaderStart != null)
            {
                XYZ delta = InPlane(view, s.LeaderStart - CurrentLeader(note).Anchor);
                if (delta.GetLength() > 1e-9)
                {
                    note.Coord = note.Coord + delta;
                    doc.Regenerate();
                }
            }

            Leader leader = CurrentLeader(note);
            XYZ anchor = leader.Anchor;
            XYZ elbow = firstNote && s.LeaderElbow != null ? s.LeaderElbow : (anchor + s.LeaderEnd) / 2;
            bool elbowUsable = elbow.DistanceTo(anchor) > 1e-6 && elbow.DistanceTo(s.LeaderEnd) > 1e-6;
            if (!elbowUsable) elbow = (anchor + s.LeaderEnd) / 2;

            // Set the end, then the bend (Revit refuses an end on top of the current bend, so try both orders).
            try { leader.End = s.LeaderEnd; leader.Elbow = elbow; }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                leader = CurrentLeader(note);
                leader.Elbow = elbow;
                leader.End = s.LeaderEnd;
            }
        }

        private static Leader CurrentLeader(TextNote note) => note.GetLeaders().Last();

        private static XYZ InPlane(View view, XYZ v) => v - view.ViewDirection * v.DotProduct(view.ViewDirection);

        private void HandleOriginals(Document doc, View view, List<TagSnapshot> converted, OriginalTagAction action, TagToTextResult result)
        {
            if (action == OriginalTagAction.Keep) return;

            if (action == OriginalTagAction.Hide)
            {
                var ids = converted.Select(s => s.TagId).Where(id => doc.GetElement(id)?.CanBeHidden(view) == true).ToList();
                if (ids.Count > 0) view.HideElements(ids);
                result.Hidden = ids.Count;
                if (ids.Count < converted.Count)
                    _log(new LogEntry(LogLevel.Warning, $"{converted.Count - ids.Count} tag(s) can't be hidden in this view and were left visible."));
                return;
            }

            var deletable = new List<ElementId>();
            foreach (var s in converted)
            {
                if (s.InGroup) _log(new LogEntry(LogLevel.Warning, $"Kept tag {s.TagId.Value}: it is in a group."));
                else if (s.OwnedBy != null) _log(new LogEntry(LogLevel.Warning, $"Kept tag {s.TagId.Value}: it is owned by {s.OwnedBy}."));
                else deletable.Add(s.TagId);
            }
            if (deletable.Count > 0) result.Deleted = doc.Delete(deletable).Count(id => deletable.Contains(id));
        }

        // Distance between the two notes of a Spot Coordinate: about 1.6 lines of the text type.
        private static double LineStep(Document doc, View view, ElementId typeId)
        {
            double size = (doc.GetElement(typeId) as TextNoteType)?.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
            if (size <= 0) size = 2.5 / 304.8;
            return TagReader.PaperToModel(view, size * 1.6);
        }
    }
}
