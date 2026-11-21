using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V031.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V031.Core.Services
{
    /// <summary>
    /// V031 NEW: reads and tidies view titles (viewport labels). Measuring needs no
    /// transaction; <see cref="ApplyToViewports"/> must run inside the Run's transaction.
    /// Only the title's position (<c>Viewport.LabelOffset</c>) and line length
    /// (<c>Viewport.LabelLineLength</c>) change. The title text is never edited.
    /// </summary>
    public static class ViewTitleService
    {
        private const double MmToFeet = 1.0 / 304.8;

        /// <summary>Reads the box, title outline and line length of <paramref name="vp"/> into <paramref name="item"/>.</summary>
        public static void Measure(Viewport vp, ViewOnSheetItem item)
        {
            Outline box = vp.GetBoxOutline();
            XYZ? tMin = null, tMax = null;
            double line = 0;
            try
            {
                // Empty or degenerate when the viewport type hides its title.
                Outline label = vp.GetLabelOutline();
                if (label != null && !label.IsEmpty)
                {
                    tMin = label.MinimumPoint;
                    tMax = label.MaximumPoint;
                }
                line = vp.LabelLineLength;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // No readable title: the box alone.
            }
            item.SetTitleGeometry(box.MinimumPoint, box.MaximumPoint, tMin, tMax, line);
        }

        /// <summary>
        /// Recomputes every item's footprint for <paramref name="settings"/>. The title band
        /// used for aligned baselines is the tallest title among <paramref name="items"/>.
        /// </summary>
        public static void ApplyLayout(IReadOnlyCollection<ViewOnSheetItem> items, ViewTitleSettings? settings)
        {
            double band = items.Count == 0 ? 0 : items.Max(i => i.TitleHeightFt);
            foreach (var item in items)
                item.ApplyTitleLayout(settings, band);
        }

        /// <summary>
        /// With aligned title baselines the views of a row must share their bottom edge, so
        /// row alignment becomes Bottom and Align rows (Align &amp; Distribute) becomes Bottom.
        /// Returns the values to pack with and a note for the log, or null when nothing changed.
        /// </summary>
        public static (RowAlignment Row, AlignDistributeSettings? Align, string? Note) EffectiveRowSettings(
            ViewTitleSettings? titles, RowAlignment rowAlignment, AlignDistributeSettings? align)
        {
            if (titles == null || !titles.BaselinesActive)
                return (rowAlignment, align, null);

            var a = align == null ? new AlignDistributeSettings() : new AlignDistributeSettings
            {
                AlignRows = align.AlignRows,
                AlignColumns = align.AlignColumns,
                DistributeH = align.DistributeH,
                DistributeV = align.DistributeV
            };
            bool changed = rowAlignment != RowAlignment.Bottom || a.AlignRows != GridRowAlign.Bottom;
            a.AlignRows = GridRowAlign.Bottom;
            return (RowAlignment.Bottom, a,
                changed ? "Title baselines aligned: rows are bottom-aligned (Align rows = Bottom)." : null);
        }

        /// <summary>
        /// Sets the line length and standard position of each title in <paramref name="items"/>,
        /// then regenerates and measures them again, so packing uses the real title sizes.
        /// A view whose title cannot be changed keeps its title and is reported in the log.
        /// </summary>
        public static void ApplyToViewports(Document doc, IReadOnlyCollection<ViewOnSheetItem> items,
            ViewTitleSettings settings, ObservableCollection<LogEntry> log)
        {
            if (!settings.StandardOffset && !settings.LineMatchesWidth)
                return;

            // Two passes, each followed by a regenerate and a fresh measurement: the line length
            // can move the title's outline, so the standard position is worked out afterwards.
            var failed = new HashSet<ElementId>();
            if (settings.LineMatchesWidth)
                ChangeTitles(doc, items, failed, log, (vp, item) =>
                {
                    if (item.BoxWidthFt > 0 && Math.Abs(vp.LabelLineLength - item.BoxWidthFt) > 1e-6)
                        vp.LabelLineLength = item.BoxWidthFt;
                });
            if (settings.StandardOffset)
                ChangeTitles(doc, items, failed, log, (vp, item) =>
                {
                    XYZ shift = item.StandardTitleShift(settings.GapBelowMm * MmToFeet);
                    if (shift.GetLength() > 1e-6)
                        vp.LabelOffset = vp.LabelOffset + shift;
                });

            int changed = items.Count(i => i.HasTitle && !failed.Contains(i.ViewportId));
            log.Add(new LogEntry(LogLevel.Info, $"View titles tidied: {changed} of {items.Count} view(s)."));
        }

        private static void ChangeTitles(Document doc, IReadOnlyCollection<ViewOnSheetItem> items, HashSet<ElementId> failed,
            ObservableCollection<LogEntry> log, Action<Viewport, ViewOnSheetItem> change)
        {
            foreach (var item in items)
            {
                ToolCancellation.CheckCurrent();
                if (!item.HasTitle || failed.Contains(item.ViewportId) || doc.GetElement(item.ViewportId) is not Viewport vp)
                    continue;
                try
                {
                    change(vp, item);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed.Add(item.ViewportId);
                    log.Add(new LogEntry(LogLevel.Warning, $"Title of '{item.ViewName}' was left as it is: {ex.Message}"));
                }
            }

            doc.Regenerate();
            foreach (var item in items)
                if (doc.GetElement(item.ViewportId) is Viewport vp)
                    Measure(vp, item);
        }
    }
}
