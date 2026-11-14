using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Services
{
    /// <summary>Everything <see cref="CombinePlanner.Build"/> needs. Revit-free.</summary>
    public sealed class CombineRequest
    {
        /// <summary>Sheet 1 as read (null until chosen).</summary>
        public SheetContent Sheet1 { get; init; }

        /// <summary>Sheet 2 as read (null until chosen).</summary>
        public SheetContent Sheet2 { get; init; }

        /// <summary>The other target sheet as read (only for <see cref="TargetMode.OtherSheet"/>).</summary>
        public SheetContent OtherTarget { get; init; }

        /// <summary>Viewports the user ticked.</summary>
        public IReadOnlyCollection<long> Ticked { get; init; } = Array.Empty<long>();

        /// <summary>Options.</summary>
        public SheetViewsCombineSettings Settings { get; init; } = new();

        /// <summary>Number for a new target sheet.</summary>
        public string NewSheetNumber { get; init; } = string.Empty;

        /// <summary>Name for a new target sheet.</summary>
        public string NewSheetName { get; init; } = string.Empty;

        /// <summary>Title block for a new target sheet.</summary>
        public long NewTitleBlockKey { get; init; }

        /// <summary>That title block's extent when placed at 0,0, or null.</summary>
        public SheetRect? NewTitleBlockFrame { get; init; }

        /// <summary>Why <see cref="NewTitleBlockFrame"/> is missing, or null.</summary>
        public string NewTitleBlockProblem { get; init; }

        /// <summary>Sheet numbers in use.</summary>
        public IReadOnlyCollection<string> ExistingSheetNumbers { get; init; } = Array.Empty<string>();
    }

    /// <summary>What happens to one view.</summary>
    public sealed class PlannedView
    {
        /// <summary>The view as read.</summary>
        public PlacedView View { get; init; }

        /// <summary>The action.</summary>
        public ViewAction Action { get; set; }

        /// <summary>Index into <see cref="CombinePlan.Sheets"/> (−1 when not placed by this run).</summary>
        public int SheetIndex { get; set; } = -1;

        /// <summary>Where the footprint goes (sheet feet), or null.</summary>
        public SheetRect? Target { get; set; }

        /// <summary>Detail number it will have, or null when unchanged / not placed.</summary>
        public string DetailNumber { get; set; }

        /// <summary>Why it is skipped, or a note.</summary>
        public string Reason { get; set; }

        /// <summary>True when a fixed legend copy overlaps something already on the sheet.</summary>
        public bool OverlapWarning { get; set; }

        /// <summary>Which slot it came from: 1 = Sheet 1, 2 = Sheet 2, 3 = the other target sheet.</summary>
        public int Slot { get; init; }

        /// <summary>Box centre X on the target (feet), from <see cref="Target"/>.</summary>
        public double CenterX => (Target?.MinX ?? 0) + View.CenterOffsetX;

        /// <summary>Box centre Y on the target (feet), from <see cref="Target"/>.</summary>
        public double CenterY => (Target?.MinY ?? 0) + View.CenterOffsetY;

        /// <summary>True when this run changes the view.</summary>
        public bool Changes => Action is ViewAction.Repack or ViewAction.Move or ViewAction.CopyLegend;
    }

    /// <summary>One sheet the views end up on.</summary>
    public sealed class PlannedSheet
    {
        /// <summary>0 = the target, 1… = continuation sheets.</summary>
        public int Index { get; init; }

        /// <summary>True when the run creates it.</summary>
        public bool IsNew { get; init; }

        /// <summary>The existing sheet (0 when <see cref="IsNew"/>).</summary>
        public long ExistingKey { get; init; }

        /// <summary>Sheet number.</summary>
        public string Number { get; init; } = string.Empty;

        /// <summary>Sheet name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Title block type for a new sheet.</summary>
        public long TitleBlockKey { get; init; }

        /// <summary>Title block extent.</summary>
        public SheetRect? Frame { get; init; }

        /// <summary>Usable area (frame inset by the margins).</summary>
        public SheetRect? Area { get; init; }

        /// <summary>Views on this sheet after the run: placed, copied and staying.</summary>
        public List<PlannedView> Views { get; } = new();

        /// <summary>Schedules and other fixed items (target only).</summary>
        public List<SheetRect> FixedItems { get; } = new();

        /// <summary>"Number - Name".</summary>
        public string Label => string.IsNullOrEmpty(Name) ? Number : $"{Number} - {Name}";
    }

    /// <summary>The whole plan, shown in the preview and carried out by the runner.</summary>
    public sealed class CombinePlan
    {
        /// <summary>Target first, then continuation sheets.</summary>
        public List<PlannedSheet> Sheets { get; } = new();

        /// <summary>Every view on Sheet 1, Sheet 2 and the target, with its action.</summary>
        public List<PlannedView> Items { get; } = new();

        /// <summary>Why the plan cannot run, or null.</summary>
        public string Blocker { get; set; }

        /// <summary>Notes for the log and the warning banner.</summary>
        public List<string> Warnings { get; } = new();

        /// <summary>Compact text of every decision; the runner refuses a plan that differs from the preview.</summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary>The target sheet, or null.</summary>
        public PlannedSheet Target => Sheets.FirstOrDefault();

        /// <summary>True when there is something to do and nothing blocks it.</summary>
        public bool CanApply => Blocker == null && Items.Any(i => i.Changes);

        /// <summary>Views coming off another sheet.</summary>
        public int MoveCount => Items.Count(i => i.Action == ViewAction.Move);

        /// <summary>Views re-packed on the target.</summary>
        public int RepackCount => Items.Count(i => i.Action == ViewAction.Repack);

        /// <summary>Legends copied.</summary>
        public int CopyCount => Items.Count(i => i.Action == ViewAction.CopyLegend);

        /// <summary>Ticked views that cannot be done.</summary>
        public int SkipCount => Items.Count(i => i.Action == ViewAction.Skip);

        /// <summary>Views staying in place on the target.</summary>
        public int StayCount => Items.Count(i => i.Action == ViewAction.StaysInPlace);
    }

    /// <summary>
    /// Builds a <see cref="CombinePlan"/>: decides each view's action, packs the incoming and
    /// re-packed views around everything that stays, spills over to continuation sheets, and
    /// gives detail numbers. Revit-free, so the window can rebuild it on every tick.
    /// </summary>
    public static class CombinePlanner
    {
        /// <summary>Millimetres per foot.</summary>
        public const double MmPerFoot = 304.8;

        /// <summary>Most continuation sheets one run may add.</summary>
        public const int MaxContinuationSheets = 50;

        /// <summary>Builds the plan for <paramref name="request"/>; never throws for bad input, it sets <see cref="CombinePlan.Blocker"/>.</summary>
        public static CombinePlan Build(CombineRequest request)
        {
            var plan = new CombinePlan();
            if (request == null)
            {
                plan.Blocker = "Nothing to plan.";
                return plan;
            }

            var s = (request.Settings ?? new SheetViewsCombineSettings()).Clone().Normalize();
            var ticked = new HashSet<long>(request.Ticked ?? Array.Empty<long>());

            if (request.Sheet1 == null)
                return Block(plan, "Choose Sheet 1.");
            if (request.Sheet2 == null)
                return Block(plan, "Choose Sheet 2.");
            if (request.Sheet1.Key == request.Sheet2.Key)
                return Block(plan, "Sheet 2 must be a different sheet from Sheet 1.");

            // ── Target ────────────────────────────────────────────────────
            SheetContent target = s.Target switch
            {
                TargetMode.Sheet1 => request.Sheet1,
                TargetMode.Sheet2 => request.Sheet2,
                TargetMode.OtherSheet => request.OtherTarget,
                _ => null
            };

            var existing = new HashSet<string>(request.ExistingSheetNumbers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            SheetRect? frame;
            long continuationTitleBlock;
            SheetRect? continuationFrame;
            PlannedSheet targetSheet;

            if (s.Target == TargetMode.NewSheet)
            {
                string number = (request.NewSheetNumber ?? string.Empty).Trim();
                if (number.Length == 0)
                    return Block(plan, "Type a number for the new sheet.");
                if (existing.Contains(number))
                    return Block(plan, $"Sheet number {number} is already used. Type another number for the new sheet.");
                if (request.NewTitleBlockKey == 0)
                    return Block(plan, "Choose a title block for the new sheet.");
                if (request.NewTitleBlockFrame is not SheetRect nf || !nf.IsValid)
                    return Block(plan, request.NewTitleBlockProblem ?? "The title block's size could not be read. Press Refresh.");
                frame = nf;
                continuationTitleBlock = request.NewTitleBlockKey;
                continuationFrame = nf;
                existing.Add(number);
                targetSheet = new PlannedSheet
                {
                    Index = 0,
                    IsNew = true,
                    Number = number,
                    Name = string.IsNullOrWhiteSpace(request.NewSheetName) ? s.NewSheetName : request.NewSheetName.Trim(),
                    TitleBlockKey = request.NewTitleBlockKey,
                    Frame = nf,
                    Area = Area(nf, s)
                };
            }
            else
            {
                if (target == null)
                    return Block(plan, "Pick the target sheet.");
                if (target.LockReason != null)
                    return Block(plan, $"The target sheet {target.Number} cannot be changed: {target.LockReason}.");
                if (target.Frame is not SheetRect tf || !tf.IsValid)
                    return Block(plan, $"The target sheet {target.Number} has no title block, so the tool cannot tell how big it is"
                                       + (target.FrameProblem != null ? $" ({target.FrameProblem})" : string.Empty)
                                       + ". Place a title block on it, or choose another target.");
                frame = tf;
                continuationTitleBlock = target.TitleBlockTypeKey;
                continuationFrame = tf.Offset(-target.TitleBlockOriginX, -target.TitleBlockOriginY);
                targetSheet = new PlannedSheet
                {
                    Index = 0,
                    ExistingKey = target.Key,
                    Number = target.Number,
                    Name = target.Name,
                    TitleBlockKey = target.TitleBlockTypeKey,
                    Frame = tf,
                    Area = Area(tf, s)
                };
                targetSheet.FixedItems.AddRange(target.FixedItems);
            }

            plan.Sheets.Add(targetSheet);
            if (targetSheet.Area is not SheetRect area || !area.IsValid)
                return Block(plan, "The margins leave no room on the sheet. Make them smaller.");

            // ── Actions ───────────────────────────────────────────────────
            var sources = new List<(SheetContent Sheet, int Slot)> { (request.Sheet1, 1), (request.Sheet2, 2) };
            if (target != null && target.Key != request.Sheet1.Key && target.Key != request.Sheet2.Key)
                sources.Add((target, 3));

            var seen = new HashSet<long>();
            var legendsOnTarget = new HashSet<long>(target?.Views.Where(v => v.IsLegend).Select(v => v.ViewKey) ?? Enumerable.Empty<long>());
            foreach (var (sheet, slot) in sources)
            {
                foreach (var view in sheet.Views)
                {
                    if (!seen.Add(view.ViewportKey))
                        continue;
                    bool onTarget = target != null && view.SheetKey == target.Key;
                    bool isTicked = ticked.Contains(view.ViewportKey) || (onTarget && s.Unticked == UntickedTargetMode.RepackAll);
                    var item = new PlannedView { View = view, Slot = slot };
                    plan.Items.Add(item);

                    if (!isTicked)
                    {
                        item.Action = onTarget ? ViewAction.StaysInPlace : ViewAction.None;
                        continue;
                    }

                    if (view.LockReason != null)
                        Skip(item, view.LockReason);
                    else if (onTarget)
                        item.Action = ViewAction.Repack;
                    else if (view.IsLegend && legendsOnTarget.Contains(view.ViewKey))
                        Skip(item, $"this legend is already on {targetSheet.Number}");
                    else if (view.IsLegend)
                        item.Action = ViewAction.CopyLegend;
                    else
                        item.Action = ViewAction.Move;
                }
            }

            // Too big for any sheet with these margins: skipped (a view on the target then stays where it is).
            foreach (var item in plan.Items.Where(i => i.Action is ViewAction.Repack or ViewAction.Move))
                if (!(item.View.Footprint.Width <= area.Width + 1e-9 && item.View.Footprint.Height <= area.Height + 1e-9))
                    Skip(item, $"it is bigger than the usable area of the sheet ({Mm(item.View.Footprint.Width)} × {Mm(item.View.Footprint.Height)} mm on paper)");

            // ── Fixed things on the target ────────────────────────────────
            var obstacles = new List<SheetRect>(targetSheet.FixedItems);
            foreach (var item in plan.Items.Where(i => target != null && i.View.SheetKey == target.Key && !i.Changes))
            {
                // Views that stay (unticked, or ticked but locked) keep their spot.
                item.SheetIndex = 0;
                item.Target = item.View.Footprint;
                targetSheet.Views.Add(item);
                obstacles.Add(item.View.Footprint);
            }

            foreach (var legend in plan.Items.Where(i => i.Action == ViewAction.CopyLegend))
            {
                var spot = legend.View.Footprint;
                legend.SheetIndex = 0;
                legend.Target = spot;
                if (obstacles.Any(o => o.Overlaps(spot)))
                {
                    legend.OverlapWarning = true;
                    plan.Warnings.Add($"Legend '{legend.View.Name}' goes on {targetSheet.Number} at the same spot as on {legend.View.SheetNumber}, where it overlaps something that stays.");
                }
                else if (!frame.Value.Contains(spot, 1e-6))
                {
                    legend.OverlapWarning = true;
                    plan.Warnings.Add($"Legend '{legend.View.Name}' sits partly outside the title block of {targetSheet.Number} at its spot from {legend.View.SheetNumber}.");
                }
                targetSheet.Views.Add(legend);
                obstacles.Add(spot);
            }

            // ── Packing ───────────────────────────────────────────────────
            double hGap = s.HorizontalGapMm / MmPerFoot, vGap = s.VerticalGapMm / MmPerFoot;
            var toPack = Order(plan.Items.Where(i => i.Action is ViewAction.Repack or ViewAction.Move), s.Order).ToList();
            var byKey = toPack.ToDictionary(i => i.View.ViewportKey);


            var pending = toPack.Select(i => new PackItem(i.View.ViewportKey, i.View.Footprint.Width, i.View.Footprint.Height)).ToList();
            var first = CombinePacker.Pack(area, obstacles, pending, hGap, vGap, s.Strategy);
            Assign(first, byKey, targetSheet);

            var left = first.Unplaced.Select(k => pending.First(p => p.Key == k)).ToList();
            int index = 1;
            while (left.Count > 0)
            {
                if (index > MaxContinuationSheets)
                {
                    foreach (var p in left)
                        Skip(byKey[p.Key], "there are more views than fit on " + MaxContinuationSheets + " extra sheets");
                    break;
                }
                if (continuationTitleBlock == 0 || continuationFrame is not SheetRect cf || !cf.IsValid)
                {
                    foreach (var p in left)
                        Skip(byKey[p.Key], $"it does not fit on {targetSheet.Number}, and an extra sheet needs a title block");
                    break;
                }

                var extra = new PlannedSheet
                {
                    Index = index,
                    IsNew = true,
                    Number = NextNumber(targetSheet.Number, index, existing),
                    Name = targetSheet.Name,
                    TitleBlockKey = continuationTitleBlock,
                    Frame = cf,
                    Area = Area(cf, s)
                };
                existing.Add(extra.Number);
                var result = CombinePacker.Pack(extra.Area.Value, Array.Empty<SheetRect>(), left, hGap, vGap, s.Strategy);
                if (result.Placed.Count == 0)
                {
                    foreach (var p in left)
                        Skip(byKey[p.Key], "it does not fit on an empty sheet with these margins");
                    break;
                }
                plan.Sheets.Add(extra);
                Assign(result, byKey, extra);
                left = result.Unplaced.Select(k => left.First(p => p.Key == k)).ToList();
                index++;
            }

            if (plan.Sheets.Count > 1)
                plan.Warnings.Add($"Not everything fits on {targetSheet.Number}: {plan.Sheets.Count - 1} extra sheet(s) will be created ({string.Join(", ", plan.Sheets.Skip(1).Select(x => x.Number))}).");

            // ── Detail numbers ────────────────────────────────────────────
            foreach (var sheet in plan.Sheets)
                NumberSheet(sheet, s.KeepDetailNumbers);

            // ── Blockers ──────────────────────────────────────────────────
            var skipped = plan.Items.Where(i => i.Action == ViewAction.Skip).ToList();
            if (s.OnFailure == FailureMode.StopAndUndo && skipped.Count > 0)
                plan.Blocker = $"\"Stop and undo everything\" is on and {skipped.Count} ticked view(s) cannot be done: "
                               + string.Join("; ", skipped.Take(3).Select(i => $"{i.View.Name} ({i.Reason})"))
                               + (skipped.Count > 3 ? " …" : string.Empty)
                               + ". Untick them, or choose \"Skip it and report\".";
            else if (!plan.Items.Any(i => i.Changes))
                plan.Blocker = ticked.Count == 0 && s.Unticked == UntickedTargetMode.KeepInPlace
                    ? "Tick the views to combine."
                    : "None of the ticked views can be placed.";

            plan.Signature = Sign(plan, s);
            return plan;
        }

        /// <summary>The usable area: <paramref name="frame"/> inset by the margins.</summary>
        public static SheetRect Area(SheetRect frame, SheetViewsCombineSettings s)
            => frame.Inset(s.MarginLeftMm / MmPerFoot, s.MarginBottomMm / MmPerFoot, s.MarginRightMm / MmPerFoot, s.MarginTopMm / MmPerFoot);

        /// <summary>The views in packing order.</summary>
        public static IEnumerable<PlannedView> Order(IEnumerable<PlannedView> items, GroupOrder order)
        {
            var list = items.ToList();
            return order switch
            {
                GroupOrder.TypeOnly => list.OrderBy(i => i.View.TypeOrder).ThenBy(i => DetailSortKey(i.View.DetailNumber)).ThenBy(i => i.View.Name, StringComparer.OrdinalIgnoreCase),
                GroupOrder.SheetThenDetail => list.OrderBy(i => i.Slot).ThenBy(i => DetailSortKey(i.View.DetailNumber)).ThenBy(i => i.View.Name, StringComparer.OrdinalIgnoreCase),
                _ => list.OrderBy(i => i.Slot).ThenBy(i => i.View.TypeOrder).ThenBy(i => DetailSortKey(i.View.DetailNumber)).ThenBy(i => i.View.Name, StringComparer.OrdinalIgnoreCase)
            };
        }

        /// <summary>Sorts "2" before "10" and numbers before text.</summary>
        public static string DetailSortKey(string detail)
        {
            detail = (detail ?? string.Empty).Trim();
            return int.TryParse(detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0
                ? "0" + n.ToString("D9", CultureInfo.InvariantCulture)
                : "1" + detail.ToUpperInvariant();
        }

        /// <summary>
        /// Detail numbers on one sheet: views that stay keep theirs, re-packed views keep theirs,
        /// incoming ones keep theirs when free (and allowed), the rest get the lowest free number.
        /// </summary>
        public static void NumberSheet(PlannedSheet sheet, bool keepWhenFree)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in sheet.Views.Where(v => !v.Changes || v.Action == ViewAction.Repack))
            {
                if (!string.IsNullOrWhiteSpace(v.View.DetailNumber))
                    taken.Add(v.View.DetailNumber.Trim());
                if (v.Action == ViewAction.Repack)
                    v.DetailNumber = v.View.DetailNumber;
            }

            var incoming = sheet.Views.Where(v => v.Action is ViewAction.CopyLegend or ViewAction.Move).ToList();
            var later = new List<PlannedView>();
            foreach (var v in incoming)
            {
                string own = (v.View.DetailNumber ?? string.Empty).Trim();
                if (keepWhenFree && own.Length > 0 && taken.Add(own))
                    v.DetailNumber = own;
                else
                    later.Add(v);
            }

            int next = 1;
            foreach (var v in later)
            {
                while (taken.Contains(next.ToString(CultureInfo.InvariantCulture)))
                    next++;
                v.DetailNumber = next.ToString(CultureInfo.InvariantCulture);
                taken.Add(v.DetailNumber);
                if (!string.IsNullOrWhiteSpace(v.View.DetailNumber) && v.View.DetailNumber.Trim() != v.DetailNumber)
                    v.Reason = $"detail {v.View.DetailNumber.Trim()} is taken on {sheet.Number}";
            }
        }

        /// <summary>A free continuation number: "A-101.1", "A-101.2" …</summary>
        public static string NextNumber(string baseNumber, int index, ISet<string> existing)
        {
            for (int n = index; ; n++)
            {
                string candidate = $"{baseNumber}.{n.ToString(CultureInfo.InvariantCulture)}";
                if (!existing.Contains(candidate))
                    return candidate;
            }
        }

        private static void Assign(PackResult result, Dictionary<long, PlannedView> byKey, PlannedSheet sheet)
        {
            foreach (var placed in result.Placed)
            {
                var item = byKey[placed.Key];
                item.SheetIndex = sheet.Index;
                item.Target = placed.Value;
                sheet.Views.Add(item);
            }
        }

        private static void Skip(PlannedView item, string reason)
        {
            item.Action = ViewAction.Skip;
            item.Reason = reason;
            item.SheetIndex = -1;
            item.Target = null;
        }

        private static CombinePlan Block(CombinePlan plan, string why)
        {
            plan.Blocker = why;
            plan.Signature = "blocked|" + why;
            return plan;
        }

        private static string Mm(double feet) => Math.Round(feet * MmPerFoot).ToString(CultureInfo.InvariantCulture);

        private static string Sign(CombinePlan plan, SheetViewsCombineSettings s)
        {
            string R(double v) => Math.Round(v * MmPerFoot, 1).ToString("0.0", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append((int)s.Target).Append('|').Append((int)s.OnFailure).Append('|').Append(s.KeepViewportType ? 1 : 0).Append('|');
            foreach (var sheet in plan.Sheets)
                sb.Append(sheet.Index).Append(':').Append(sheet.ExistingKey).Append(':').Append(sheet.Number).Append(':').Append(sheet.TitleBlockKey).Append(';');
            foreach (var i in plan.Items.OrderBy(i => i.View.ViewportKey))
            {
                sb.Append(i.View.ViewportKey).Append('=').Append((int)i.Action).Append('@').Append(i.SheetIndex);
                if (i.Target is SheetRect t)
                    sb.Append('(').Append(R(t.MinX)).Append(',').Append(R(t.MinY)).Append(',').Append(R(t.Width)).Append(',').Append(R(t.Height)).Append(')');
                sb.Append('#').Append(i.DetailNumber).Append(';');
            }
            return sb.ToString();
        }
    }
}
