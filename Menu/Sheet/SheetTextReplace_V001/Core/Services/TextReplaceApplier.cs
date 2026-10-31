using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services
{
    /// <summary>
    /// Changes the ticked text notes in one TransactionGroup + Transaction (one Undo step). Each note
    /// is checked again first and skipped when it changed since the preview, was deleted, or is now
    /// owned by someone else. Any unexpected error rolls everything back.
    /// </summary>
    public static class TextReplaceApplier
    {
        /// <summary>Name of the Undo step in Revit.</summary>
        public const string TransactionName = "Sheet Text Replace";

        /// <summary>Applies <paramref name="request"/>. Never throws; errors come back in the result.</summary>
        public static ApplyResult Apply(Document doc, ApplyRequest request)
        {
            var watch = Stopwatch.StartNew();
            var changed = new List<long>();
            var skipped = new List<NoteIssue>();
            var failed = new List<NoteIssue>();
            var formattingReset = new List<NoteIssue>();

            if (request.Changes.Count == 0)
                return Error("Nothing to change: tick at least one row in the preview.");
            if (doc.IsReadOnly)
                return Error("The model is read-only. Nothing was changed.");

            using var group = new TransactionGroup(doc, TransactionName);
            try
            {
                group.Start();
                using (var t = new Transaction(doc, TransactionName))
                {
                    t.Start();
                    foreach (var change in request.Changes)
                    {
                        if (doc.GetElement(new ElementId(change.NoteId)) is not TextNote note)
                        {
                            skipped.Add(Issue(change, "Deleted since preview"));
                            continue;
                        }

                        string reason = SheetTextReader.BlockReason(doc, note, note.OwnerViewId);
                        if (reason != null)
                        {
                            skipped.Add(Issue(change, reason));
                            continue;
                        }

                        if ((note.Text ?? string.Empty) != change.ExpectedText)
                        {
                            skipped.Add(Issue(change, "Changed since preview"));
                            continue;
                        }

                        try
                        {
                            if (!TextNoteWriter.Write(note, change))
                                formattingReset.Add(Issue(change, "Formatting could not be kept; the note was set as plain text"));
                            changed.Add(change.NoteId);
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                        {
                            failed.Add(Issue(change, ex.Message));
                        }
                        catch (ArgumentException ex)
                        {
                            failed.Add(Issue(change, ex.Message));
                        }
                    }

                    if (changed.Count == 0)
                    {
                        t.RollBack();
                        group.RollBack();
                        return Result(true, null, changed, skipped, failed, formattingReset, watch);
                    }

                    var status = t.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        group.RollBack();
                        return Error($"Revit did not accept the changes ({status}). Nothing was changed.");
                    }
                }

                group.Assimilate();
                return Result(true, null, changed, skipped, failed, formattingReset, watch);
            }
            catch (Exception ex)
            {
                if (group.HasStarted() && !group.HasEnded())
                    group.RollBack();
                ToolGuard.Report(typeof(TextReplaceApplier), ex);
                return Error("Unexpected error: " + ex.Message + " Nothing was changed. Details are in the plugin log.");
            }
        }

        private static NoteIssue Issue(NoteChange c, string reason) => new(c.NoteId, c.ViewName, c.ExpectedText, reason);

        private static ApplyResult Error(string message) => new() { Success = false, Error = message };

        private static ApplyResult Result(bool ok, string error, List<long> changed, List<NoteIssue> skipped,
            List<NoteIssue> failed, List<NoteIssue> formattingReset, Stopwatch watch) => new()
        {
            Success = ok,
            Error = error,
            ChangedIds = changed,
            Skipped = skipped,
            Failed = failed,
            FormattingReset = formattingReset,
            ElapsedMs = watch.ElapsedMilliseconds
        };
    }
}
