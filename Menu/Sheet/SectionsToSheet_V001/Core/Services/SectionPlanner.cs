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

        /// <summary>
        /// The existing sheet chosen as the target, as read (used when
        /// <see cref="SectionsToSheetSettings.Target"/> is <see cref="TargetMode.ExistingSheet"/>), or null.
        /// </summary>
        public TargetSheetContent Target { get; init; }

        /// <summary>Why <see cref="Target"/> is null (no sheet chosen yet, still reading …).</summary>
        public string TargetProblem { get; init; }
    }

    /// <summary>
    /// One view as it will be placed: a ticked section (<see cref="Section"/>), or a view already on
    /// the existing target sheet that is re-arranged (<see cref="Existing"/>).
    /// </summary>
    public sealed class PlannedViewport
    {
        /// <summary>The ticked section, or null for a re-arranged view of the target sheet.</summary>
        public SectionCandidate Section { get; init; }

        /// <summary>The viewport already on the target sheet that moves, or null for a ticked section.</summary>
        public TargetItem Existing { get; init; }

        /// <summary>0-based sheet in <see cref="SectionPlan.Sheets"/>.</summary>
        public int Sheet { get; init; }

        /// <summary>0-based row on that sheet.</summary>
        public int Row { get; init; }

        /// <summary>0-based position in the row.</summary>
        public int Column { get; init; }

        /// <summary>Detail number given to a ticked section; 0 for a re-arranged view (it keeps its own).</summary>
        public int DetailNumber { get; init; }

        /// <summary>Footprint (box ∪ title) on the sheet, feet.</summary>
        public LayoutRect Target { get; init; }

        /// <summary>Viewport box centre to place at, X (feet).</summary>
        public double CenterX { get; init; }

        /// <summary>Viewport box centre to place at, Y (feet).</summary>
        public double CenterY { get; init; }

        /// <summary>True for a view already on the target sheet that moves to a new position.</summary>
        public bool IsRearranged => Existing != null;

        /// <summary>True when a ticked section comes off another sheet.</summary>
        public bool IsMove => Section != null && Section.IsOnSheet;

        /// <summary>The view's element id value.</summary>
        public long ViewKey => Existing?.ViewKey ?? Section?.Key ?? 0;

        /// <summary>View name.</summary>
        public string Name => Existing?.Name ?? Section?.Name ?? string.Empty;

        /// <summary>Scale denominator, or 0.</summary>
        public int Scale => Existing?.Scale ?? Section?.Scale ?? 0;

        /// <summary>The detail number as it will read on the sheet.</summary>
        public string DetailLabel => Existing != null
            ? Existing.DetailNumber ?? string.Empty
            : DetailNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One sheet of the plan: the existing target sheet, or a new one.</summary>
    public sealed class PlannedSheet
    {
        /// <summary>0-based index.</summary>
        public int Index { get; init; }

        /// <summary>The existing sheet's element id value; 0 for a sheet the run creates.</summary>
        public long ExistingKey { get; init; }

        /// <summary>True for the existing target sheet.</summary>
        public bool IsExisting => ExistingKey != 0;

        /// <summary>The title block's extent on this sheet (feet), or null.</summary>
        public LayoutRect? Frame { get; init; }

        /// <summary>Usable area: the frame inset by the margins (feet), or null.</summary>
        public LayoutRect? Area { get; init; }

        /// <summary>Existing sheet: the empty rectangle the views were laid out in, or null.</summary>
        public LayoutRect? FreeArea { get; init; }

        /// <summary>Existing sheet: items that stay where they are (schedules, text, views in free-space mode …).</summary>
        public List<TargetItem> Obstacles { get; } = new();

        /// <summary>Sheet number.</summary>
        public string Number { get; init; }

        /// <summary>Sheet name.</summary>
        public string Name { get; init; }

        /// <summary>Views on it, in reading order.</summary>
        public List<PlannedViewport> Viewports { get; } = new();

        /// <summary>Rows on it.</summary>
        public int RowCount { get; init; }

        /// <summary>Footprint area (and items that stay) ÷ usable area, %.</summary>
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

        /// <summary>True when nothing is wrong: the section is already on the target sheet and stays there.</summary>
        public bool IsNote { get; init; }
    }

    /// <summary>The full result of planning: sheets, what moves, what is skipped, and whether it can run.</summary>
    public sealed class SectionPlan
    {
        /// <summary>The sheets, in order: the existing target sheet first (when there is one), then new ones.</summary>
        public List<PlannedSheet> Sheets { get; } = new();

        /// <summary>Ticked sections that will not be placed.</summary>
        public List<SkippedSection> Skipped { get; } = new();

        /// <summary>Old sheets left with no views and no schedules after the move.</summary>
        public List<SheetContent> SheetsLeftEmpty { get; } = new();

        /// <summary>Non-blocking notes (mixed scales …).</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>The first sheet's title block extent (feet), or null.</summary>
        public LayoutRect? Frame { get; init; }

        /// <summary>The first sheet's usable area: the frame inset by the margins (feet), or null.</summary>
        public LayoutRect? Area { get; init; }

        /// <summary>The existing target sheet, or null for a new-sheet run.</summary>
        public TargetSheetContent Target { get; init; }

        /// <summary>What happens to what is on the existing target sheet.</summary>
        public ExistingSheetMode ExistingMode { get; init; }

        /// <summary>Columns in Grid mode (on the first sheet), 0 otherwise.</summary>
        public int Columns { get; set; }

        /// <summary>Why nothing can be created, or null.</summary>
        public string Blocker { get; set; }

        /// <summary>
        /// Identifies the outcome: two plans with the same signature create the same sheets and
        /// place the same views at the same points. Used to refuse a run when the model changed
        /// after the preview.
        /// </summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>Every placed or re-arranged view, sheet by sheet.</summary>
        public IEnumerable<PlannedViewport> Viewports => Sheets.SelectMany(s => s.Viewports);

        /// <summary>Ticked sections placed, sheet by sheet.</summary>
        public IEnumerable<PlannedViewport> Placed => Viewports.Where(v => !v.IsRearranged);

        /// <summary>Views already on the target sheet that move to new positions.</summary>
        public List<PlannedViewport> Rearranged => Viewports.Where(v => v.IsRearranged).ToList();

        /// <summary>Views that come off another sheet.</summary>
        public List<PlannedViewport> Moves => Viewports.Where(v => v.IsMove).ToList();

        /// <summary>Ticked sections placed.</summary>
        public int PlacedCount => Placed.Count();

        /// <summary>Sheets the run creates.</summary>
        public int NewSheetCount => Sheets.Count(s => !s.IsExisting);

        /// <summary>True when the run can go ahead.</summary>
        public bool CanApply => Blocker == null && PlacedCount > 0;
    }

    /// <summary>
    /// Turns the ticked sections, their measured footprints, the settings and (for an existing
    /// target sheet) what is already on that sheet into a <see cref="SectionPlan"/>. No Revit API
    /// calls — the live preview and the run use the same plan, so the preview always shows exactly
    /// what the run does.
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
            bool toExisting = s.Target == TargetMode.ExistingSheet;
            var target = toExisting ? request.Target : null;
            bool rearrange = toExisting && s.ExistingMode == ExistingSheetMode.Rearrange;

            // ── Which sections can take part ────────────────────────────
            var skipped = new List<SkippedSection>();
            var usable = new List<SectionCandidate>();
            foreach (var section in request.Sections ?? Array.Empty<SectionCandidate>())
            {
                string reason = null;
                bool note = false;
                if (section.LockReason != null)
                    reason = "Can’t move: " + section.LockReason;
                else if (target != null && section.CurrentSheetKey == target.Key)
                {
                    note = true;
                    reason = rearrange
                        ? $"Already on {target.Number}: re-arranged with the sheet’s views"
                        : $"Already on {target.Number}: stays where it is";
                }
                else if (section.IsOnSheet && !s.RemoveFromOldSheets)
                    reason = $"Already on {section.CurrentSheetNumber} (removing from the old sheet is off)";
                else if (request.MeasureProblems != null && request.MeasureProblems.TryGetValue(section.Key, out var problem))
                    reason = problem;
                else if (request.Footprints == null || !request.Footprints.ContainsKey(section.Key))
                    reason = "Not measured yet";

                if (reason != null)
                    skipped.Add(new SkippedSection { Section = section, Reason = reason, IsNote = note });
                else
                    usable.Add(section);
            }

            var ordered = Order(usable, s, request.SourceViewScale, request.Footprints);
            double hGap = Math.Max(0, s.MinHorizontalGapMm) / MmPerFoot;
            double vGap = Math.Max(0, s.MinVerticalGapMm) / MmPerFoot;
            var options = new GridLayoutOptions
            {
                Mode = s.Mode,
                MinHorizontalGap = hGap,
                MinVerticalGap = vGap,
                Vertical = s.Vertical,
                LastRow = s.LastRow,
                SinglePage = s.Overflow == OverflowMode.Skip
            };

            // ── Usable areas ────────────────────────────────────────────
            LayoutRect? newFrame = request.TitleBlockFrame is LayoutRect f && f.IsValid ? f : null;
            LayoutRect? newArea = Inset(newFrame, s);
            LayoutRect? targetFrame = target?.Frame is LayoutRect tf && tf.IsValid ? tf : null;
            LayoutRect? targetArea = Inset(targetFrame, s);

            string blocker = null;
            if (toExisting)
            {
                if (target == null)
                    blocker = request.TargetProblem ?? "Choose the sheet to place the sections on.";
                else if (target.LockReason != null)
                    blocker = $"Can’t place on {target.Number}: {target.LockReason}.";
                else if (targetFrame == null)
                    blocker = target.FrameProblem ?? $"The title block of {target.Number} could not be read.";
                else if (targetArea == null)
                    blocker = $"The margins leave no usable area on {target.Number}.";
            }
            else if (newFrame == null)
                blocker = request.FrameProblem ?? "The title block’s extent could not be read.";
            else if (newArea == null)
                blocker = "The margins leave no usable area on this title block.";

            var plan = new SectionPlan
            {
                Frame = toExisting ? targetFrame : newFrame,
                Area = toExisting ? targetArea : newArea,
                Target = target,
                ExistingMode = s.ExistingMode
            };
            var warnings = new List<string>();
            int columns = 0;

            // ── The existing target sheet (page 0) ──────────────────────
            var leftovers = ordered;
            if (blocker == null && toExisting)
            {
                var viewports = target.Items.Where(i => i.IsViewport).ToList();
                var movable = rearrange
                    ? SortOnSheet(viewports.Where(i => i.LockReason == null).ToList(), s.RowToleranceMm / MmPerFoot)
                    : new List<TargetItem>();
                var stays = target.Items.Where(i => !movable.Contains(i)).ToList();
                foreach (var locked in viewports.Where(i => rearrange && i.LockReason != null))
                    warnings.Add($"{locked.Name} stays where it is on {target.Number}: {locked.LockReason}.");

                LayoutRect? free = FreeSpace.Largest(targetArea.Value, stays.Select(i => i.Rect.Grow(hGap, vGap)));
                var sheet = new PlannedSheetBuilder(0, target.Key, target.Number, target.Name, targetFrame, targetArea, free, stays);
                int detail = HighestDetailNumber(viewports);

                if (free == null)
                {
                    if (movable.Count > 0)
                        blocker = $"No room left on {target.Number} for its views: the items that stay fill the usable area.";
                }
                else
                {
                    var sizes = movable.Select(i => (i.Rect.Width, i.Rect.Height))
                        .Concat(ordered.Select(c => (request.Footprints[c.Key].Width, request.Footprints[c.Key].Height)))
                        .ToList();
                    var layout = SheetGridLayout.Compute(sizes, free.Value, new GridLayoutOptions
                    {
                        Mode = options.Mode, MinHorizontalGap = hGap, MinVerticalGap = vGap,
                        Vertical = options.Vertical, LastRow = options.LastRow, SinglePage = true
                    });
                    columns = layout.Columns;

                    var notFitting = layout.TooBig.Concat(layout.NoRoom).Where(i => i < movable.Count).Select(i => movable[i].Name).ToList();
                    if (notFitting.Count > 0)
                        blocker = $"The views already on {target.Number} don’t fit in the space its other items leave " +
                                  $"({Mm(free.Value.Width)} × {Mm(free.Value.Height)} mm): {string.Join(", ", notFitting)}. " +
                                  "Use “Use free space” instead, or reduce the margins.";

                    var placedHere = new HashSet<int>();
                    if (layout.Pages.Count > 0)
                    {
                        var page = layout.Pages[0];
                        sheet.RowCount = page.RowCount;
                        sheet.HorizontalGap = page.HorizontalGap;
                        sheet.VerticalGap = page.VerticalGap;
                        foreach (var slot in page.Slots)
                        {
                            placedHere.Add(slot.Index);
                            if (slot.Index < movable.Count)
                            {
                                var item = movable[slot.Index];
                                sheet.Viewports.Add(new PlannedViewport
                                {
                                    Existing = item, Sheet = 0, Row = slot.Row, Column = slot.Column,
                                    Target = LayoutRect.FromCorner(slot.MinX, slot.MinY, item.Rect.Width, item.Rect.Height),
                                    CenterX = slot.MinX + (item.BoxCenterX - item.Rect.MinX),
                                    CenterY = slot.MinY + (item.BoxCenterY - item.Rect.MinY)
                                });
                            }
                            else
                            {
                                sheet.Viewports.Add(Place(ordered[slot.Index - movable.Count], request.Footprints, 0, slot, ++detail));
                            }
                        }
                    }
                    leftovers = ordered.Where((c, i) => !placedHere.Contains(i + movable.Count)).ToList();
                }
                plan.Sheets.Add(sheet.Build(request.Footprints));
            }

            // ── New sheets ──────────────────────────────────────────────
            if (blocker == null && leftovers.Count > 0)
            {
                if (toExisting && s.Overflow == OverflowMode.Skip)
                {
                    foreach (var c in leftovers)
                        skipped.Add(new SkippedSection { Section = c, Reason = $"No room on {target.Number} (“If it doesn’t fit” is set to skip)" });
                }
                else if (toExisting && newArea == null)
                {
                    string why = newFrame == null ? request.FrameProblem ?? "the title block’s extent could not be read" : "the margins leave no usable area on its title block";
                    foreach (var c in leftovers)
                        skipped.Add(new SkippedSection { Section = c, Reason = $"No room on {target.Number}, and no extra sheet: {why.TrimEnd('.')}" });
                }
                else
                {
                    var sizes = leftovers.Select(c => (request.Footprints[c.Key].Width, request.Footprints[c.Key].Height)).ToList();
                    var layout = SheetGridLayout.Compute(sizes, newArea.Value, options);
                    if (!toExisting || columns == 0)
                        columns = layout.Columns;

                    foreach (int i in layout.TooBig)
                    {
                        var fp = request.Footprints[leftovers[i].Key];
                        skipped.Add(new SkippedSection { Section = leftovers[i], Reason = $"Bigger than the usable area ({Mm(fp.Width)} × {Mm(fp.Height)} mm)" });
                    }
                    foreach (int i in layout.NoRoom)
                        skipped.Add(new SkippedSection { Section = leftovers[i], Reason = "No room on one sheet (“If it doesn’t fit” is set to skip)" });

                    string first = (request.FirstSheetNumber ?? string.Empty).Trim();
                    string name = (request.SheetName ?? string.Empty).Trim();
                    var numbers = SheetNumbering.Sequence(first, layout.Pages.Count, request.ExistingSheetNumbers);
                    if (layout.Pages.Count > 0)
                    {
                        var existing = new HashSet<string>(request.ExistingSheetNumbers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                        string extra = toExisting ? "extra " : string.Empty;
                        if (first.Length == 0)
                            blocker = $"Type a number for the {extra}new sheet.";
                        else if (existing.Contains(first))
                            blocker = $"Sheet number {first} already exists. Type another number for the {extra}new sheet.";
                        else if (name.Length == 0)
                            blocker = $"Type a name for the {extra}new sheet.";
                    }

                    for (int p = 0; p < layout.Pages.Count; p++)
                    {
                        int index = plan.Sheets.Count;
                        var page = layout.Pages[p];
                        var sheet = new PlannedSheetBuilder(index, 0, p < numbers.Count ? numbers[p] : string.Empty,
                            p == 0 ? name : $"{name} ({p + 1})", newFrame, newArea, null, null)
                        {
                            RowCount = page.RowCount,
                            HorizontalGap = page.HorizontalGap,
                            VerticalGap = page.VerticalGap
                        };
                        int detail = 0;
                        foreach (var slot in page.Slots)
                            sheet.Viewports.Add(Place(leftovers[slot.Index], request.Footprints, index, slot, ++detail));
                        plan.Sheets.Add(sheet.Build(request.Footprints));
                    }
                }
            }

            plan.Columns = columns;
            plan.Skipped.AddRange(skipped);

            // ── Old sheets left empty ───────────────────────────────────
            foreach (var group in plan.Moves.GroupBy(m => m.Section.CurrentSheetKey))
            {
                if (request.Sheets != null && request.Sheets.TryGetValue(group.Key, out var content)
                    && content.ViewportCount - group.Count() <= 0 && content.ScheduleCount == 0)
                    plan.SheetsLeftEmpty.Add(content);
            }

            // ── Notes ───────────────────────────────────────────────────
            plan.Warnings.AddRange(warnings);
            var scales = plan.Viewports.GroupBy(v => v.Scale).OrderBy(g => g.Key == 0 ? int.MaxValue : g.Key).ToList();
            if (scales.Count > 1)
                plan.Warnings.Add($"Mixed scales {(target != null ? "on " + target.Number : "on the new sheet")}: " +
                                  string.Join(", ", scales.Select(g => $"{(g.Key > 0 ? "1:" + g.Key : "unknown")} ×{g.Count()}")) + ".");

            if (blocker == null && plan.PlacedCount == 0)
            {
                if ((request.Sections?.Count ?? 0) == 0)
                    blocker = "Tick at least one section.";
                else if (usable.Count == 0)
                    blocker = "None of the ticked sections can be placed — the list says why.";
                else if (toExisting)
                    blocker = $"Nothing fits on {target.Number}. Reduce the margins, or let extra sheets be added.";
                else
                    blocker = "Nothing fits in the usable area. Reduce the margins or use a larger title block.";
            }

            plan.Blocker = blocker;
            plan.Signature = BuildSignature(plan, s);
            return plan;
        }

        /// <summary>
        /// The highest plain-number detail number among <paramref name="viewports"/> (0 when none);
        /// new sections on the sheet continue after it. Numbers like "A3" are left out.
        /// </summary>
        public static int HighestDetailNumber(IEnumerable<TargetItem> viewports)
        {
            int best = 0;
            foreach (var item in viewports ?? Enumerable.Empty<TargetItem>())
                if (int.TryParse(item.DetailNumber?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > best)
                    best = n;
            return best;
        }

        /// <summary>Viewports in reading order on the sheet: top to bottom in row bands, left to right.</summary>
        private static List<TargetItem> SortOnSheet(List<TargetItem> items, double tolerance)
        {
            var order = ReadingOrder.Sort(items.Select(i => new ReadingItem((i.Rect.MinX + i.Rect.MaxX) / 2, i.Rect.MaxY, i.Name)).ToList(), tolerance);
            return order.Select(i => items[i]).ToList();
        }

        private static LayoutRect? Inset(LayoutRect? frame, SectionsToSheetSettings s)
        {
            if (frame is not LayoutRect f)
                return null;
            var area = f.Inset(s.MarginLeftMm / MmPerFoot, s.MarginBottomMm / MmPerFoot, s.MarginRightMm / MmPerFoot, s.MarginTopMm / MmPerFoot);
            return area.IsValid ? area : null;
        }

        private static PlannedViewport Place(SectionCandidate section, IReadOnlyDictionary<long, Footprint> footprints, int sheet, GridSlot slot, int detail)
        {
            var fp = footprints[section.Key];
            return new PlannedViewport
            {
                Section = section,
                Sheet = sheet,
                Row = slot.Row,
                Column = slot.Column,
                DetailNumber = detail,
                Target = LayoutRect.FromCorner(slot.MinX, slot.MinY, fp.Width, fp.Height),
                CenterX = slot.MinX + fp.CenterOffsetX,
                CenterY = slot.MinY + fp.CenterOffsetY
            };
        }

        /// <summary>Collects one sheet's viewports, then works out its fill.</summary>
        private sealed class PlannedSheetBuilder
        {
            private readonly int _index;
            private readonly long _existingKey;
            private readonly string _number, _name;
            private readonly LayoutRect? _frame, _area, _free;
            private readonly List<TargetItem> _obstacles;

            public PlannedSheetBuilder(int index, long existingKey, string number, string name,
                LayoutRect? frame, LayoutRect? area, LayoutRect? free, List<TargetItem> obstacles)
            {
                _index = index; _existingKey = existingKey; _number = number; _name = name;
                _frame = frame; _area = area; _free = free; _obstacles = obstacles ?? new List<TargetItem>();
            }

            public List<PlannedViewport> Viewports { get; } = new();
            public int RowCount { get; set; }
            public double? HorizontalGap { get; set; }
            public double? VerticalGap { get; set; }

            public PlannedSheet Build(IReadOnlyDictionary<long, Footprint> footprints)
            {
                double areaSize = _area?.Area ?? 0;
                double used = Viewports.Sum(v => v.Target.Area)
                              + (_area is LayoutRect a ? _obstacles.Sum(o => o.Rect.Intersect(a)?.Area ?? 0) : 0);
                var sheet = new PlannedSheet
                {
                    Index = _index,
                    ExistingKey = _existingKey,
                    Number = _number,
                    Name = _name,
                    Frame = _frame,
                    Area = _area,
                    FreeArea = _free,
                    RowCount = RowCount,
                    HorizontalGap = HorizontalGap,
                    VerticalGap = VerticalGap,
                    FillPercent = areaSize > 0 ? Math.Min(100, used / areaSize * 100) : 0
                };
                sheet.Obstacles.AddRange(_obstacles);
                sheet.Viewports.AddRange(Viewports);
                return sheet;
            }
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
            if (plan.Target != null)
                sb.Append("on:").Append(plan.Target.Key).Append(plan.ExistingMode == ExistingSheetMode.Rearrange ? "/R" : "/F").Append('|');
            foreach (var sheet in plan.Sheets)
            {
                sb.Append('[').Append(sheet.IsExisting ? "#" + sheet.ExistingKey : sheet.Number).Append('|').Append(sheet.Name).Append(']');
                foreach (var v in sheet.Viewports)
                    sb.Append(v.IsRearranged ? "R" + v.Existing.Key : v.Section.Key.ToString(CultureInfo.InvariantCulture))
                      .Append(':').Append(v.DetailLabel).Append('@')
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
