using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services
{
    /// <summary>Outcome of <see cref="PlacementRunner.Apply"/>.</summary>
    public sealed class RunResult
    {
        /// <summary>True when the transaction committed.</summary>
        public bool Success { get; init; }

        /// <summary>Why it failed (nothing was changed), when <see cref="Success"/> is false.</summary>
        public string Error { get; init; }

        /// <summary>New sheets, in order.</summary>
        public List<ElementId> CreatedSheets { get; init; } = new();

        /// <summary>Viewports placed.</summary>
        public int Placed { get; init; }

        /// <summary>Viewports removed from old sheets.</summary>
        public int Removed { get; init; }

        /// <summary>Numbers of old sheets deleted because they ended up empty.</summary>
        public List<string> DeletedSheets { get; init; } = new();

        /// <summary>Empty old sheets kept, with the reason.</summary>
        public List<string> KeptSheets { get; init; } = new();
    }

    /// <summary>
    /// Carries out a <see cref="SectionPlan"/> in one transaction: takes the moving views off their
    /// old sheets, creates the new sheets, places the views with their detail numbers, and
    /// (optionally) deletes old sheets left empty. Any failure rolls everything back — the model
    /// is never left half done.
    /// </summary>
    public static class PlacementRunner
    {
        /// <summary>Name of the step in Revit's Undo list.</summary>
        public const string TransactionName = "Sections To Sheet";

        /// <summary>
        /// Applies <paramref name="plan"/> (the caller has checked <see cref="SectionPlan.CanApply"/>).
        /// <paramref name="activeViewId"/> is never deleted.
        /// </summary>
        public static RunResult Apply(Document doc, SectionPlan plan, long titleBlockKey, long viewportTypeKey,
            bool deleteEmptySheets, ElementId activeViewId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.CanApply)
                return new RunResult { Error = plan.Blocker ?? "Nothing to place." };
            if (doc.IsReadOnly)
                return new RunResult { Error = "The document is read-only." };

            var titleBlock = doc.GetElement(new ElementId(titleBlockKey)) as FamilySymbol;
            if (titleBlock == null)
                return new RunResult { Error = "The chosen title block is no longer in the project. Press Refresh." };
            var viewportType = viewportTypeKey != 0 ? new ElementId(viewportTypeKey) : ElementId.InvalidElementId;

            using var tx = new Transaction(doc, TransactionName);
            var failures = QuietFailures.Attach(tx);
            try
            {
                if (tx.Start() != TransactionStatus.Started)
                    return new RunResult { Error = "Revit could not start the change." };

                // 1. Off the old sheets (a view can be on one sheet only).
                int removed = 0;
                foreach (var move in plan.Moves)
                    if (FootprintMeasurer.RemoveExistingViewport(doc, new ElementId(move.Section.Key)))
                        removed++;
                doc.Regenerate();

                // 2. New sheets and viewports.
                if (!titleBlock.IsActive)
                    titleBlock.Activate();

                var created = new List<ElementId>();
                int placed = 0;
                foreach (var plannedSheet in plan.Sheets)
                {
                    var sheet = ViewSheet.Create(doc, titleBlock.Id);
                    sheet.SheetNumber = plannedSheet.Number;
                    sheet.Name = plannedSheet.Name;
                    created.Add(sheet.Id);

                    var viewports = new List<(Viewport Viewport, int Detail)>();
                    foreach (var planned in plannedSheet.Viewports)
                    {
                        var viewId = new ElementId(planned.Section.Key);
                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                            throw new InvalidOperationException($"Revit can’t put '{planned.Section.Name}' on sheet {plannedSheet.Number}.");

                        var vp = Viewport.Create(doc, sheet.Id, viewId, new XYZ(planned.CenterX, planned.CenterY, 0))
                                 ?? throw new InvalidOperationException($"Revit did not create a viewport for '{planned.Section.Name}'.");
                        if (viewportType != ElementId.InvalidElementId && vp.GetTypeId() != viewportType && vp.IsValidType(viewportType))
                            vp.ChangeTypeId(viewportType);
                        viewports.Add((vp, planned.DetailNumber));
                        placed++;
                    }

                    SetDetailNumbers(viewports);
                }

                // 3. Old sheets left empty.
                var deleted = new List<string>();
                var kept = new List<string>();
                if (deleteEmptySheets)
                {
                    foreach (var content in plan.SheetsLeftEmpty)
                    {
                        if (doc.GetElement(new ElementId(content.Key)) is not ViewSheet old)
                            continue;
                        string why = content.LockReason
                                     ?? (old.Id == activeViewId ? "it is the active view" : null)
                                     ?? (old.GetAllViewports().Count > 0 || HasSchedules(doc, old) ? "it still holds views or schedules" : null);
                        if (why != null)
                        {
                            kept.Add($"{content.Number} ({why})");
                            continue;
                        }
                        doc.Delete(old.Id);
                        deleted.Add(content.Number);
                    }
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return new RunResult
                    {
                        Error = "Revit refused the change, nothing was changed" +
                                (failures.Errors.Count > 0 ? ": " + string.Join(" ", failures.Errors.Distinct()) : ".")
                    };

                return new RunResult
                {
                    Success = true,
                    CreatedSheets = created,
                    Placed = placed,
                    Removed = removed,
                    DeletedSheets = deleted,
                    KeptSheets = kept
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return new RunResult { Error = ex.Message + " Nothing was changed." };
            }
        }

        /// <summary>
        /// Gives the viewports their detail numbers. Revit numbers new viewports itself; to avoid
        /// clashes while renumbering, every one first gets a temporary unique number.
        /// </summary>
        private static void SetDetailNumbers(List<(Viewport Viewport, int Detail)> viewports)
        {
            string Wanted(int detail) => detail.ToString(CultureInfo.InvariantCulture);

            var toChange = viewports
                .Where(v => v.Viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() != Wanted(v.Detail))
                .ToList();
            if (toChange.Count == 0)
                return;

            foreach (var (vp, _) in toChange)
                vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.Set("~" + vp.Id.Value.ToString(CultureInfo.InvariantCulture));
            foreach (var (vp, detail) in toChange)
                vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.Set(Wanted(detail));
        }

        private static bool HasSchedules(Document doc, ViewSheet sheet)
            => new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>().Any(s => !s.IsTitleblockRevisionSchedule);
    }
}
