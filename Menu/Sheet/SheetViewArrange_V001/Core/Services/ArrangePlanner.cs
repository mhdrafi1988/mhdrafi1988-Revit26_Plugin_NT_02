using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Services
{
    /// <summary>One viewport and the footprint it should end up at.</summary>
    public sealed class PlannedMove
    {
        /// <summary>The viewport as read from the sheet.</summary>
        public ViewportSnapshot Viewport { get; init; }

        /// <summary>Target footprint in sheet feet.</summary>
        public LayoutRect Target { get; init; }

        /// <summary>0-based row.</summary>
        public int Row { get; init; }

        /// <summary>False when the target lies outside the usable area.</summary>
        public bool Fits { get; init; }

        /// <summary>Translation from the current footprint to the target (feet).</summary>
        public double DeltaX => Target.MinX - Viewport.Footprint.MinX;

        /// <summary>Translation from the current footprint to the target (feet).</summary>
        public double DeltaY => Target.MinY - Viewport.Footprint.MinY;

        /// <summary>True when the viewport actually has to move.</summary>
        public bool NeedsMove => Math.Abs(DeltaX) > ArrangePlanner.MoveTolerance || Math.Abs(DeltaY) > ArrangePlanner.MoveTolerance;
    }

    /// <summary>The full arrangement for one sheet: what moves where, and whether it can be applied.</summary>
    public sealed class ArrangePlan
    {
        /// <summary>The sheet the plan was made from.</summary>
        public SheetSnapshot Sheet { get; init; }

        /// <summary>Usable area (title block inset by margins), or null when there is none.</summary>
        public LayoutRect? Area { get; init; }

        /// <summary>Participating viewports in reading order.</summary>
        public List<PlannedMove> Moves { get; init; } = new();

        /// <summary>Viewports left where they are.</summary>
        public List<ViewportSnapshot> Skipped { get; init; } = new();

        /// <summary>Grid rows: every view in reading order, each with its slot when it is arranged.</summary>
        public List<ArrangeRow> Rows { get; init; } = new();

        /// <summary>Number of layout rows.</summary>
        public int RowCount { get; init; }

        /// <summary>Why the plan cannot be applied, or null when it can.</summary>
        public string Blocker { get; init; }

        /// <summary>Non-blocking issues worth telling the user about.</summary>
        public List<string> Warnings { get; init; } = new();

        /// <summary>Viewports that will actually move.</summary>
        public int MoveCount => Moves.Count(m => m.NeedsMove);

        /// <summary>True when Apply would do something and nothing blocks it.</summary>
        public bool CanApply => Blocker == null && MoveCount > 0;

        /// <summary>
        /// Every move as "id:dx,dy". Two plans with the same signature move the same viewports by
        /// the same amounts — used to refuse an Apply when the sheet changed after the preview.
        /// </summary>
        public string Signature
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var m in Moves.Where(m => m.NeedsMove))
                    sb.Append(m.Viewport.ViewportId?.Value.ToString(CultureInfo.InvariantCulture))
                      .Append(':').Append(Math.Round(m.DeltaX, 5).ToString(CultureInfo.InvariantCulture))
                      .Append(',').Append(Math.Round(m.DeltaY, 5).ToString(CultureInfo.InvariantCulture))
                      .Append(';');
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Turns a <see cref="SheetSnapshot"/> plus settings into an <see cref="ArrangePlan"/>.
    /// No Revit API calls — used for both the live preview and the Apply step, so the
    /// preview always shows exactly what Apply does.
    /// </summary>
    public static class ArrangePlanner
    {
        /// <summary>Moves smaller than this (feet, ≈ 0.003 mm) are treated as "already in place".</summary>
        public const double MoveTolerance = 1e-5;

        private const double MmToFeet = 1.0 / 304.8;
        private const double FeetToMm = 304.8;

        /// <summary>Why a view the user unticked is left where it is.</summary>
        public const string UntickedReason = "Unticked";

        /// <summary>
        /// Builds the plan. <paramref name="unticked"/> holds the <see cref="ViewportSnapshot.Key"/>
        /// of every view the user unticked: those stay where they are and the others are laid out
        /// without them. Null or empty (the default) means every view takes part, as before.
        /// </summary>
        public static ArrangePlan Build(SheetSnapshot sheet, SheetViewArrangeSettings settings, IReadOnlySet<long> unticked = null)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var (all, participants, skipped) = SplitParticipants(sheet, settings.MovePinned, unticked);
            var warnings = CollectWarnings(sheet, participants, skipped);
            var (area, blocker) = UsableArea(sheet, settings);

            var moves = new List<PlannedMove>();
            int rowCount = 0;
            if (area is LayoutRect usable && participants.Count > 0)
            {
                var layout = ReadingTableLayout.Compute(
                    participants.Select(v => (v.Footprint.Width, v.Footprint.Height)).ToList(),
                    usable,
                    new LayoutOptions
                    {
                        MinHorizontalGap = settings.MinHorizontalGapMm * MmToFeet,
                        MinVerticalGap = settings.MinVerticalGapMm * MmToFeet,
                        LastRowMode = settings.LastRowMode,
                        Horizontal = settings.Horizontal,
                        Vertical = settings.Vertical,
                        RowAlign = settings.RowAlign,
                        Flow = settings.Flow,
                        ColumnAlign = settings.ColumnAlign
                    });
                rowCount = layout.RowCount;

                foreach (var slot in layout.Slots)
                {
                    var vp = participants[slot.Index];
                    moves.Add(new PlannedMove
                    {
                        Viewport = vp,
                        Target = new LayoutRect(slot.MinX, slot.MinY, slot.MinX + vp.Footprint.Width, slot.MinY + vp.Footprint.Height),
                        Row = slot.Row,
                        Fits = slot.Fits
                    });
                }

                if (!layout.AllFit)
                    blocker = "Does not fit: " + layout.Problem + " Reduce margins or gaps, or move some views to another sheet.";
            }
            else if (blocker == null)
            {
                blocker = sheet.Viewports.Count == 0
                    ? "There are no views on this sheet."
                    : "Every view on this sheet is skipped — nothing to arrange.";
            }

            return new ArrangePlan
            {
                Sheet = sheet,
                Area = area,
                Moves = moves,
                Skipped = skipped.Select(s => s.Viewport).ToList(),
                Rows = BuildRows(all, participants, moves, skipped, settings.MovePinned,
                                 settings.Flow == FlowMode.Columns),
                RowCount = rowCount,
                Blocker = blocker,
                Warnings = warnings
            };
        }

        /// <summary>True when the user may tick or untick the view (it is not left alone for another reason).</summary>
        private static bool CanTick(ViewportSnapshot vp, bool movePinned)
            => vp.LockReason == null && !(vp.IsPinned && !movePinned);

        /// <summary>Why the view is left where it is, or null when it takes part. A lock or pin reason wins over "Unticked".</summary>
        private static string SkipReason(ViewportSnapshot vp, bool movePinned, IReadOnlySet<long> unticked)
        {
            if (vp.LockReason != null)
                return vp.LockReason;
            if (vp.IsPinned && !movePinned)
                return "Pinned";
            return unticked != null && unticked.Contains(vp.Key) ? UntickedReason : null;
        }

        /// <summary>
        /// Every view in reading order (detail number, then view name); the ones that take part, in
        /// that order; and the ones left where they are with the reason.
        /// </summary>
        private static (List<ViewportSnapshot> All, List<ViewportSnapshot> Participants, List<(ViewportSnapshot Viewport, string Why)> Skipped)
            SplitParticipants(SheetSnapshot sheet, bool movePinned, IReadOnlySet<long> unticked)
        {
            var all = sheet.Viewports
                .OrderBy(v => v.DetailNumber, DetailNumberComparer.Instance)
                .ThenBy(v => v.ViewName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var participants = all.Where(v => SkipReason(v, movePinned, unticked) == null).ToList();

            // Skipped views keep the order they have always had (detail number only, ties in sheet
            // order) so the warning text reads exactly as before.
            var skipped = sheet.Viewports
                .Select(v => (Viewport: v, Why: SkipReason(v, movePinned, unticked)))
                .Where(s => s.Why != null)
                .OrderBy(s => s.Viewport.DetailNumber, DetailNumberComparer.Instance)
                .ToList();

            return (all, participants, skipped);
        }

        private static List<string> CollectWarnings(
            SheetSnapshot sheet,
            List<ViewportSnapshot> participants,
            List<(ViewportSnapshot Viewport, string Why)> skipped)
        {
            var warnings = new List<string>();

            int blank = participants.Count(v => string.IsNullOrWhiteSpace(v.DetailNumber));
            if (blank > 0)
                warnings.Add($"{blank} view(s) have no detail number — placed last, ordered by view name.");

            var duplicates = participants
                .Where(v => !string.IsNullOrWhiteSpace(v.DetailNumber))
                .GroupBy(v => v.DetailNumber, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
                warnings.Add($"Duplicate detail number(s): {string.Join(", ", duplicates)} — ordered by view name.");

            if (skipped.Count > 0)
                warnings.Add($"{skipped.Count} view(s) stay where they are ({string.Join("; ", skipped.Select(s => s.Why).Distinct())}); arranged views may overlap them.");

            if (sheet.ScheduleCount > 0)
                warnings.Add($"{sheet.ScheduleCount} schedule(s) on the sheet are not moved; arranged views may overlap them.");

            if (sheet.UnreadableCount > 0)
                warnings.Add($"{sheet.UnreadableCount} viewport(s) could not be read and are not moved; arranged views may overlap them.");

            return warnings;
        }

        /// <summary>Title block frame inset by the margins, or the reason there is no usable area.</summary>
        private static (LayoutRect? Area, string Blocker) UsableArea(SheetSnapshot sheet, SheetViewArrangeSettings settings)
        {
            if (sheet.TitleBlockFrame is not LayoutRect frame)
                return (null, sheet.FrameProblem ?? "No usable title block on this sheet.");

            var inset = new LayoutRect(
                frame.MinX + Math.Max(0, settings.MarginLeftMm) * MmToFeet,
                frame.MinY + Math.Max(0, settings.MarginBottomMm) * MmToFeet,
                frame.MaxX - Math.Max(0, settings.MarginRightMm) * MmToFeet,
                frame.MaxY - Math.Max(0, settings.MarginTopMm) * MmToFeet);

            return inset.IsValid
                ? (inset, null)
                : (null, "The margins leave no usable area inside the title block — reduce them.");
        }

        /// <summary>
        /// Grid rows for every view, in reading order — so a view keeps its place in the list when
        /// it is unticked. Arranged views carry their slot; skipped ones have no "#" or row.
        /// </summary>
        private static List<ArrangeRow> BuildRows(
            List<ViewportSnapshot> all,
            List<ViewportSnapshot> participants,
            List<PlannedMove> moves,
            List<(ViewportSnapshot Viewport, string Why)> skipped,
            bool movePinned,
            bool bandIsColumn)
        {
            // Moves are in participant order; look both up by viewport (reference equality).
            var arranged = new Dictionary<ViewportSnapshot, (int Order, PlannedMove Move)>();
            for (int i = 0; i < participants.Count; i++)
                arranged[participants[i]] = (i + 1, moves.Count > 0 ? moves[i] : null);
            var skipWhy = new Dictionary<ViewportSnapshot, string>();
            foreach (var (vp, why) in skipped)
                skipWhy[vp] = why;

            var rows = new List<ArrangeRow>(all.Count);
            for (int k = 0; k < all.Count; k++)
            {
                var vp = all[k];
                bool canTick = CanTick(vp, movePinned);

                if (arranged.TryGetValue(vp, out var a))
                {
                    var m = a.Move;
                    ArrangeStatus status = m == null || !m.Fits ? ArrangeStatus.DoesNotFit
                                         : m.NeedsMove ? ArrangeStatus.Move
                                         : ArrangeStatus.InPlace;
                    string note = m == null ? "No usable area"
                                : !m.Fits ? "Outside the usable area"
                                : !m.NeedsMove ? ""
                                : vp.IsPinned ? "Stays pinned" : "";

                    rows.Add(NewRow(vp, k, a.Order, m == null ? null : m.Row + 1, status, note, canTick, isTicked: true, bandIsColumn));
                }
                else
                {
                    rows.Add(NewRow(vp, k, null, null, ArrangeStatus.Skipped, skipWhy.TryGetValue(vp, out var why) ? why : "", canTick, isTicked: false, bandIsColumn));
                }
            }

            return rows;
        }

        private static ArrangeRow NewRow(
            ViewportSnapshot vp, int readingKey, int? order, int? row, ArrangeStatus status, string note, bool canTick, bool isTicked,
            bool bandIsColumn = false) => new()
        {
            ViewportKey = vp.Key,
            ReadingKey = readingKey,
            Order = order,
            DetailNumber = vp.DetailNumber,
            ViewName = vp.ViewName,
            ViewTypeName = vp.ViewTypeName,
            SizeText = $"{vp.Footprint.Width * FeetToMm:F0} × {vp.Footprint.Height * FeetToMm:F0}",
            SizeArea = vp.Footprint.Width * FeetToMm * vp.Footprint.Height * FeetToMm,
            Row = row,
            BandIsColumn = bandIsColumn,
            Status = status,
            Note = note,
            CanTick = canTick,
            IsTicked = isTicked
        };
    }
}
