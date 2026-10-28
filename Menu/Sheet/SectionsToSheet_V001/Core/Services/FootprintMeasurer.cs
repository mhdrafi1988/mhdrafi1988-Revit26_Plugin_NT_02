using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services
{
    /// <summary>
    /// Measures how big each section is on a sheet — the viewport box plus its title, with the
    /// chosen viewport type — and the chosen title block's extent. It places everything on a
    /// temporary sheet inside a transaction that is always rolled back, so the model is never
    /// changed. A section that is on a sheet now is measured as it will be after the move.
    /// </summary>
    public static class FootprintMeasurer
    {
        /// <summary>
        /// Measures <paramref name="sections"/> (locked ones are skipped). Must run in Revit's API
        /// context with no transaction open.
        /// </summary>
        public static MeasureResult Measure(Document doc, long titleBlockKey, long viewportTypeKey, IEnumerable<SectionCandidate> sections)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var list = (sections ?? Array.Empty<SectionCandidate>()).Where(s => s.LockReason == null).ToList();
            var footprints = new Dictionary<long, Footprint>();
            var problems = new Dictionary<long, string>();

            if (doc.IsReadOnly || doc.IsModifiable)
            {
                string why = doc.IsReadOnly ? "The document is read-only." : "Revit is busy with another change.";
                foreach (var s in list) problems[s.Key] = why;
                return new MeasureResult { TitleBlockKey = titleBlockKey, ViewportTypeKey = viewportTypeKey, FrameProblem = why, Footprints = footprints, Problems = problems };
            }

            LayoutRect? frame = null;
            string frameProblem = null;
            var titleBlock = titleBlockKey != 0 ? doc.GetElement(new ElementId(titleBlockKey)) as FamilySymbol : null;
            var viewportType = viewportTypeKey != 0 ? new ElementId(viewportTypeKey) : ElementId.InvalidElementId;
            if (titleBlock == null)
                frameProblem = "Choose a title block. If the list is empty, load a title block family into the project and press Refresh.";

            using var tx = new Transaction(doc, "Sections To Sheet - measure");
            QuietFailures.Attach(tx);
            try
            {
                if (tx.Start() != TransactionStatus.Started)
                {
                    foreach (var s in list) problems[s.Key] = "Revit could not start the measuring step.";
                    return new MeasureResult { TitleBlockKey = titleBlockKey, ViewportTypeKey = viewportTypeKey, FrameProblem = frameProblem ?? "Revit could not start the measuring step.", Footprints = footprints, Problems = problems };
                }

                if (titleBlock != null && !titleBlock.IsActive)
                    titleBlock.Activate();
                ViewSheet sheet = ViewSheet.Create(doc, titleBlock?.Id ?? ElementId.InvalidElementId);
                doc.Regenerate();

                if (titleBlock != null)
                    (frame, frameProblem) = ReadFrame(doc, sheet);

                // Views on a sheet now are measured as they will be after the move: off their old sheet first.
                foreach (var section in list.Where(s => s.IsOnSheet))
                {
                    try
                    {
                        RemoveExistingViewport(doc, new ElementId(section.Key));
                    }
                    catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is ArgumentException || ex is InvalidOperationException)
                    {
                        problems[section.Key] = "Could not be taken off its sheet: " + ex.Message;
                    }
                }
                doc.Regenerate();

                var created = new List<(SectionCandidate Section, Viewport Viewport)>();
                foreach (var section in list.Where(s => !problems.ContainsKey(s.Key)))
                {
                    try
                    {
                        var viewId = new ElementId(section.Key);
                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
                        {
                            problems[section.Key] = "Revit can’t put this view on a sheet (it may show nothing yet)";
                            continue;
                        }

                        var vp = Viewport.Create(doc, sheet.Id, viewId, XYZ.Zero);
                        if (vp == null)
                        {
                            problems[section.Key] = "Revit did not create a viewport for this view";
                            continue;
                        }
                        if (viewportType != ElementId.InvalidElementId && vp.GetTypeId() != viewportType && vp.IsValidType(viewportType))
                            vp.ChangeTypeId(viewportType);
                        created.Add((section, vp));
                    }
                    catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is ArgumentException || ex is InvalidOperationException)
                    {
                        problems[section.Key] = "Could not be measured: " + ex.Message;
                    }
                }

                doc.Regenerate();

                foreach (var (section, vp) in created)
                {
                    try
                    {
                        var fp = ReadFootprint(vp);
                        if (fp == null)
                            problems[section.Key] = "Its size on the sheet could not be read";
                        else
                            footprints[section.Key] = fp;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        problems[section.Key] = "Its size on the sheet could not be read: " + ex.Message;
                    }
                }
            }
            finally
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
            }

            return new MeasureResult
            {
                TitleBlockKey = titleBlockKey,
                ViewportTypeKey = viewportTypeKey,
                Frame = frame,
                FrameProblem = frame == null ? frameProblem ?? "The title block’s extent could not be read." : null,
                Footprints = footprints,
                Problems = problems
            };
        }

        /// <summary>Deletes the viewport that shows <paramref name="viewId"/>, if there is one (unpinning it first).</summary>
        public static bool RemoveExistingViewport(Document doc, ElementId viewId)
        {
            var vp = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                .FirstOrDefault(v => v.ViewId == viewId);
            if (vp == null)
                return false;
            if (vp.Pinned)
                vp.Pinned = false;
            doc.Delete(vp.Id);
            return true;
        }

        /// <summary>The viewport box united with its title, and where the box centre sits in it.</summary>
        private static Footprint ReadFootprint(Viewport vp)
        {
            Outline box = vp.GetBoxOutline();
            if (box == null || box.IsEmpty)
                return null;

            double minX = box.MinimumPoint.X, minY = box.MinimumPoint.Y;
            double maxX = box.MaximumPoint.X, maxY = box.MaximumPoint.Y;
            try
            {
                // Empty or degenerate when the viewport type hides its title.
                Outline label = vp.GetLabelOutline();
                if (label != null && !label.IsEmpty
                    && label.MaximumPoint.X > label.MinimumPoint.X && label.MaximumPoint.Y > label.MinimumPoint.Y)
                {
                    minX = Math.Min(minX, label.MinimumPoint.X);
                    minY = Math.Min(minY, label.MinimumPoint.Y);
                    maxX = Math.Max(maxX, label.MaximumPoint.X);
                    maxY = Math.Max(maxY, label.MaximumPoint.Y);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // No readable title — the box alone.
            }

            if (!(maxX > minX) || !(maxY > minY))
                return null;

            XYZ centre = vp.GetBoxCenter();
            return new Footprint
            {
                Width = maxX - minX,
                Height = maxY - minY,
                CenterOffsetX = centre.X - minX,
                CenterOffsetY = centre.Y - minY
            };
        }

        private static (LayoutRect? Frame, string Problem) ReadFrame(Document doc, ViewSheet sheet)
        {
            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().ToElements();
            if (titleBlocks.Count == 0)
                return (null, "The title block was not placed on the new sheet.");

            BoundingBoxXYZ bb = titleBlocks[0].get_BoundingBox(sheet);
            if (bb == null)
                return (null, "The title block’s extent could not be read.");

            var rect = new LayoutRect(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
            return rect.IsValid ? (rect, null) : (null, "The title block has no usable extent.");
        }
    }

    /// <summary>
    /// Failure handling for this tool's transactions: warnings are dismissed; errors are
    /// collected and the transaction rolls back silently, so the tool can report them itself.
    /// </summary>
    public sealed class QuietFailures : IFailuresPreprocessor
    {
        /// <summary>Error messages Revit reported, if any.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Attaches a new instance to <paramref name="tx"/> and returns it.</summary>
        public static QuietFailures Attach(Transaction tx)
        {
            var handler = new QuietFailures();
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(handler);
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
            return handler;
        }

        /// <inheritdoc/>
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasErrors = false;
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    hasErrors = true;
                    Errors.Add(failure.GetDescriptionText());
                }
            }

            if (!hasErrors)
                return FailureProcessingResult.Continue;

            var options = failuresAccessor.GetFailureHandlingOptions();
            options.SetClearAfterRollback(true);
            failuresAccessor.SetFailureHandlingOptions(options);
            return FailureProcessingResult.ProceedWithRollBack;
        }
    }
}
