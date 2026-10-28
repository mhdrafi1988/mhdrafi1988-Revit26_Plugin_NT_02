using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services
{
    /// <summary>Everything <see cref="SectionPlanner.Build"/> needs, read from the model beforehand.</summary>
    public sealed class PlanRequest
    {
        /// <summary>The ticked sections, any order (the planner sorts them).</summary>
        public IReadOnlyList<SectionCandidate> Sections { get; init; } = Array.Empty<SectionCandidate>();

        /// <summary>Measured footprints by section key, for the chosen viewport type.</summary>
        public IReadOnlyDictionary<long, Footprint> Footprints { get; init; } = new Dictionary<long, Footprint>();

        /// <summary>Why a section could not be measured or placed, by key.</summary>
        public IReadOnlyDictionary<long, string> MeasureProblems { get; init; } = new Dictionary<long, string>();

        /// <summary>The chosen title block's extent on a new sheet (feet), or null.</summary>
        public LayoutRect? TitleBlockFrame { get; init; }

        /// <summary>Why <see cref="TitleBlockFrame"/> is null.</summary>
        public string FrameProblem { get; init; }

        /// <summary>The sheets the sections are on now, by key.</summary>
        public IReadOnlyDictionary<long, SheetContent> Sheets { get; init; } = new Dictionary<long, SheetContent>();

        /// <summary>Every sheet number in the model.</summary>
        public IReadOnlyCollection<string> ExistingSheetNumbers { get; init; } = Array.Empty<string>();

        /// <summary>Scale of the source view (for the plan-order row tolerance).</summary>
        public int SourceViewScale { get; init; } = 1;

        /// <summary>Number for the first new sheet, as typed.</summary>
        public string FirstSheetNumber { get; init; } = string.Empty;

        /// <summary>Name for the new sheets, as typed.</summary>
        public string SheetName { get; init; } = string.Empty;

        /// <summary>Layout options.</summary>
        public SectionsToSheetSettings Settings { get; init; } = new();
    }

    /// <summary>One view as it will be placed.</summary>
    public sealed class PlannedViewport
    {
        /// <summary>The section.</summary>
        public SectionCandidate Section { get; init; }

        /// <summary>0-based new sheet.</summary>
        public int Sheet { get; init; }

        /// <summary>0-based row on that sheet.</summary>
        public int Row { get; init; }

        /// <summary>0-based position in the row.</summary>
        public int Column { get; init; }

        /// <summary>Detail number on the new sheet (1, 2, 3 … in reading order).</summary>
        public int DetailNumber { get; init; }

        /// <summary>Footprint (box ∪ title) on the new sheet, feet.</summary>
        public LayoutRect Target { get; init; }

        /// <summary>Viewport box centre to place at, X (feet).</summary>
        public double CenterX { get; init; }

        /// <summary>Viewport box centre to place at, Y (feet).</summary>
        public double CenterY { get; init; }

        /// <summary>True when the view comes off another sheet.</summary>
        public bool IsMove => Section.IsOnSheet;
    }

    /// <summary>One new sheet.</summary>
    public sealed class PlannedSheet
    {
        /// <summary>0-based index.</summary>
        public int Index { get; init; }

        /// <summary>Sheet number.</summary>
        public string Number { get; init; }

        /// <summary>Sheet name.</summary>
        public string Name { get; init; }

        /// <summary>Views on it, in reading order.</summary>
        public List<PlannedViewport> Viewports { get; } = new();

        /// <summary>Rows on it.</summary>
        public int RowCount { get; init; }

        /// <summary>Footprint area ÷ usable area, %.</summary>
        public double FillPercent { get; init; }

        /// <summary>Smallest gap between neighbours in a row (feet), or null.</summary>
        public double? HorizontalGap { get; init; }

        /// <summary>Gap between rows (feet), or null for one row.</summary>
        public double? VerticalGap { get; init; }
    }

    /// <summary>A ticked section that will not be placed, and why.</summary>
    public sealed class SkippedSection
    {
        /// <summary>The section.</summary>
        public SectionCandidate Section { get; init; }

        /// <summary>Why it is skipped, for the list and the log.</summary>
        public string Reason { get; init; }
    }

    /// <summary>The full result of planning: new sheets, what moves, what is skipped, and whether it can run.</summary>
    public sealed class SectionPlan
    {
        /// <summary>New sheets, in order.</summary>
        public List<PlannedSheet> Sheets { get; } = new();

        /// <summary>Ticked sections that will not be placed.</summary>
        public List<SkippedSection> Skipped { get; } = new();

        /// <summary>Old sheets left with no views and no schedules after the move.</summary>
        public List<SheetContent> SheetsLeftEmpty { get; } = new();

        /// <summary>Non-blocking notes (mixed scales …).</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>The title block's extent (feet), or null.</summary>
        public LayoutRect? Frame { get; init; }

        /// <summary>Usable area: the frame inset by the margins (feet), or null.</summary>
        public LayoutRect? Area { get; init; }

        /// <summary>Columns in Grid mode, 0 otherwise.</summary>
        public int Columns { get; init; }

        /// <summary>Why nothing can be created, or null.</summary>
        public string Blocker { get; set; }

        /// <summary>
        /// Identifies the outcome: two plans with the same signature create the same sheets and
        /// place the same views at the same points. Used to refuse a run when the model changed
        /// after the preview.
        /// </summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>Every placed view, sheet by sheet.</summary>
        public IEnumerable<PlannedViewport> Viewports => Sheets.SelectMany(s => s.Viewports);

        /// <summary>Views that come off another sheet.</summary>
        public List<PlannedViewport> Moves => Viewports.Where(v => v.IsMove).ToList();

        /// <summary>Views placed.</summary>
        public int PlacedCount => Sheets.Sum(s => s.Viewports.Count);

        /// <summary>True when the run can go ahead.</summary>
        public bool CanApply => Blocker == null && PlacedCount > 0;
    }

    /// <summary>
    /// Turns the ticked sections, their measured footprints and the settings into a
    /// <see cref="SectionPlan"/>. No Revit API calls — the live preview and the run use the same
    /// plan, so the preview always shows exactly what the run does.
    /// </summary>
    public static class SectionPlanner
    {
        /// <summary>Millimetres per foot.</summary>
        public const double MmPerFoot = 304.8;

        /// <summary>Builds the plan.</summary>
        public static SectionPlan Build(PlanRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var s = request.Settings ?? new SectionsToSheetSettings();

            // ── Which sections can take part ────────────────────────────
            var skipped = new List<SkippedSection>();
            var usable = new List<SectionCandidate>();
            foreach (var section in request.Sections ?? Array.Empty<SectionCandidate>())
            {
                string reason = null;
                if (section.LockReason != null)
                    reason = "Can’t move: " + section.LockReason;
                else if (section.IsOnSheet && !s.RemoveFromOldSheets)
                    reason = $"Already on {section.CurrentSheetNumber} (removing from the old sheet is off)";
                else if (request.MeasureProblems != null && request.MeasureProblems.TryGetValue(section.Key, out var problem))
                    reason = problem;
                else if (request.Footprints == null || !request.Footprints.ContainsKey(section.Key))
                    reason = "Not measured yet";

                if (reason != null)
                    skipped.Add(new SkippedSection { Section = section, Reason = reason });
                else
                    usable.Add(section);
            }

            var ordered = Order(usable, s, request.SourceViewScale, request.Footprints);

            // ── Usable area ─────────────────────────────────────────────
            LayoutRect? frame = request.TitleBlockFrame is LayoutRect f && f.IsValid ? f : null;
            LayoutRect? area = frame?.Inset(s.MarginLeftMm / MmPerFoot, s.MarginBottomMm / MmPerFoot,
                                            s.MarginRightMm / MmPerFoot, s.MarginTopMm / MmPerFoot);
            string blocker = null;
            if (frame == null)
                blocker = request.FrameProblem ?? "The title block’s extent could not be read.";
            else if (!area.Value.IsValid)
                blocker = "The margins leave no usable area on this title block.";

            GridLayoutResult layout = null;
            if (blocker == null && ordered.Count > 0)
            {
                var sizes = ordered.Select(c => (request.Footprints[c.Key].Width, request.Footprints[c.Key].Height)).ToList();
                layout = SheetGridLayout.Compute(sizes, area.Value, new GridLayoutOptions
                {
                    Mode = s.Mode,
                    MinHorizontalGap = s.MinHorizontalGapMm / MmPerFoot,
                    MinVerticalGap = s.MinVerticalGapMm / MmPerFoot,
                    Vertical = s.Vertical,
                    LastRow = s.LastRow,
                    SinglePage = s.Overflow == OverflowMode.Skip
                });

                foreach (int i in layout.TooBig)
                {
                    var fp = request.Footprints[ordered[i].Key];
                    skipped.Add(new SkippedSection
                    {
                        Section = ordered[i],
                        Reason = $"Bigger than the usable area ({Mm(fp.Width)} × {Mm(fp.Height)} mm)"
                    });
                }
                foreach (int i in layout.NoRoom)
                    skipped.Add(new SkippedSection { Section = ordered[i], Reason = "No room on one sheet (“If it doesn’t fit” is set to skip)" });
            }

            var plan = new SectionPlan
            {
                Frame = frame,
                Area = area is LayoutRect a && a.IsValid ? a : null,
                Columns = layout?.Columns ?? 0
            };
            plan.Skipped.AddRange(skipped);

            // ── Sheets and viewports ────────────────────────────────────
            int pageCount = layout?.Pages.Count ?? 0;
            string first = (request.FirstSheetNumber ?? string.Empty).Trim();
            string name = (request.SheetName ?? string.Empty).Trim();
            var numbers = SheetNumbering.Sequence(first, pageCount, request.ExistingSheetNumbers);

            if (blocker == null && pageCount > 0)
            {
                var existing = new HashSet<string>(request.ExistingSheetNumbers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                if (first.Length == 0)
                    blocker = "Type a number for the new sheet.";
                else if (existing.Contains(first))
                    blocker = $"Sheet number {first} already exists. Type another number.";
                else if (name.Length == 0)
                    blocker = "Type a name for the new sheet.";
            }

            for (int p = 0; p < pageCount; p++)
            {
                var page = layout.Pages[p];
                double used = page.Slots.Sum(slot =>
                {
                    var fp = request.Footprints[ordered[slot.Index].Key];
                    return fp.Width * fp.Height;
                });
                double areaSize = area.Value.Width * area.Value.Height;
                var sheet = new PlannedSheet
                {
                    Index = p,
                    Number = p < numbers.Count ? numbers[p] : string.Empty,
                    Name = p == 0 ? name : $"{name} ({p + 1})",
                    RowCount = page.RowCount,
                    HorizontalGap = page.HorizontalGap,
                    VerticalGap = page.VerticalGap,
                    FillPercent = areaSize > 0 ? used / areaSize * 100 : 0
                };

                int detail = 0;
                foreach (var slot in page.Slots)
                {
                    var section = ordered[slot.Index];
                    var fp = request.Footprints[section.Key];
                    sheet.Viewports.Add(new PlannedViewport
                    {
                        Section = section,
                        Sheet = p,
                        Row = slot.Row,
                        Column = slot.Column,
                        DetailNumber = ++detail,
                        Target = LayoutRect.FromCorner(slot.MinX, slot.MinY, fp.Width, fp.Height),
                        CenterX = slot.MinX + fp.CenterOffsetX,
                        CenterY = slot.MinY + fp.CenterOffsetY
                    });
                }

                plan.Sheets.Add(sheet);
            }

            // ── Old sheets left empty ───────────────────────────────────
            foreach (var group in plan.Moves.GroupBy(m => m.Section.CurrentSheetKey))
            {
                if (request.Sheets != null && request.Sheets.TryGetValue(group.Key, out var content)
                    && content.ViewportCount - group.Count() <= 0 && content.ScheduleCount == 0)
                    plan.SheetsLeftEmpty.Add(content);
            }

            // ── Notes ───────────────────────────────────────────────────
            var scales = plan.Viewports.GroupBy(v => v.Section.Scale).OrderBy(g => g.Key == 0 ? int.MaxValue : g.Key).ToList();
            if (scales.Count > 1)
                plan.Warnings.Add("Mixed scales on the new sheet: " +
                                  string.Join(", ", scales.Select(g => $"{(g.Key > 0 ? "1:" + g.Key : "unknown")} ×{g.Count()}")) + ".");

            if (blocker == null && plan.PlacedCount == 0)
            {
                if ((request.Sections?.Count ?? 0) == 0)
                    blocker = "Tick at least one section.";
                else if (usable.Count == 0)
                    blocker = "None of the ticked sections can be placed — the list says why.";
                else
                    blocker = "Nothing fits in the usable area. Reduce the margins or use a larger title block.";
            }

            plan.Blocker = blocker;
            plan.Signature = BuildSignature(plan, s);
            return plan;
        }

        /// <summary>
        /// The sections in placement order. <paramref name="footprints"/> is only needed for
        /// <see cref="SectionOrder.Size"/>; sections without one count as size 0.
        /// </summary>
        public static List<SectionCandidate> Order(IReadOnlyList<SectionCandidate> sections, SectionsToSheetSettings settings,
            int sourceViewScale, IReadOnlyDictionary<long, Footprint> footprints = null)
        {
            var list = (sections ?? Array.Empty<SectionCandidate>()).ToList();
            double tolerance = (settings?.RowToleranceMm ?? 10) / MmPerFoot * Math.Max(1, sourceViewScale);
            var planOrder = ReadingOrder.Sort(list.Select(c => new ReadingItem(c.MarkerAcross, c.MarkerUp, c.Name)).ToList(), tolerance);
            var rank = new Dictionary<SectionCandidate, int>();
            for (int i = 0; i < planOrder.Count; i++)
                rank[list[planOrder[i]]] = i;

            double Size(SectionCandidate c)
                => footprints != null && footprints.TryGetValue(c.Key, out var fp) ? fp.Width * fp.Height : 0;

            var names = NaturalStringComparer.Instance;
            return (settings?.Order ?? SectionOrder.Plan) switch
            {
                SectionOrder.Number => list.OrderBy(c => c.Name, names).ThenBy(c => rank[c]).ToList(),
                SectionOrder.Scale => list.OrderBy(c => c.Scale > 0 ? c.Scale : int.MaxValue).ThenBy(c => rank[c]).ToList(),
                SectionOrder.Size => list.OrderByDescending(Size).ThenBy(c => rank[c]).ToList(),
                _ => planOrder.Select(i => list[i]).ToList()
            };
        }

        private static string BuildSignature(SectionPlan plan, SectionsToSheetSettings s)
        {
            var sb = new StringBuilder();
            foreach (var sheet in plan.Sheets)
            {
                sb.Append('[').Append(sheet.Number).Append('|').Append(sheet.Name).Append(']');
                foreach (var v in sheet.Viewports)
                    sb.Append(v.Section.Key).Append(':').Append(v.DetailNumber).Append('@')
                      .Append(v.CenterX.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                      .Append(v.CenterY.ToString("F6", CultureInfo.InvariantCulture))
                      .Append(v.IsMove ? "<" + v.Section.CurrentSheetKey : string.Empty).Append(';');
            }
            if (s.DeleteEmptySheets)
                sb.Append("del:").Append(string.Join(",", plan.SheetsLeftEmpty.Select(x => x.Key)));
            sb.Append("|skip:").Append(string.Join(",", plan.Skipped.Select(x => x.Section.Key)));
            return sb.ToString();
        }

        private static string Mm(double feet) => Math.Round(feet * MmPerFoot).ToString("0", CultureInfo.InvariantCulture);
    }
}
