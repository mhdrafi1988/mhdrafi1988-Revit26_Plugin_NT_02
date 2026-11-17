using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services
{
    /// <summary>
    /// Turns a sheet snapshot plus the user's settings into an <see cref="AlignPlan"/>: which
    /// elements take part, what the usable area is, and what <see cref="AlignEngine"/> decided for
    /// each one. The layout maths all live in the engine; this adds the Revit-side rules about
    /// which elements may be touched at all.
    /// </summary>
    public static class AlignPlanner
    {
        /// <summary>
        /// Builds the plan. <paramref name="unticked"/> holds the element keys the user left out;
        /// they are listed but never moved, and still take up room in the overlap check.
        /// </summary>
        public static AlignPlan Build(SheetSnapshot snapshot, AlignDistributeSettings settings,
            IReadOnlySet<long> unticked)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            unticked ??= new HashSet<long>();

            var detected = Detect(snapshot, settings);

            if (snapshot.TitleBlockFrame is not LayoutRect frame)
                return Stopped(snapshot, detected, snapshot.FrameProblem
                    ?? "The usable area is measured from the title block, and this sheet's could not be read.");

            LayoutRect usable = frame.Inset(
                Units.MmToFeet(settings.MarginLeftMm), Units.MmToFeet(settings.MarginBottomMm),
                Units.MmToFeet(settings.MarginRightMm), Units.MmToFeet(settings.MarginTopMm));

            if (!usable.IsValid)
                return Stopped(snapshot, detected,
                    "The margins leave no room inside the title block. Lower them and try again.");

            if (snapshot.Elements.Count == 0)
                return Stopped(snapshot, detected, "There is nothing on this sheet to align.");

            // ── Who takes part ─────────────────────────────────────────────
            var participants = new List<SheetElementSnapshot>();
            var frozen = new List<SheetElementSnapshot>();
            var skipReason = new Dictionary<long, string>();

            foreach (var element in snapshot.Elements)
            {
                string reason = SkipReason(element, settings, unticked);
                if (reason == null)
                {
                    participants.Add(element);
                }
                else
                {
                    frozen.Add(element);
                    skipReason[element.Key] = reason;
                }
            }

            if (participants.Count == 0)
                return Stopped(snapshot, detected, "No element on this sheet is ticked and movable.", frozen, skipReason);

            // ── Plan ───────────────────────────────────────────────────────
            var boxes = participants
                .Select((element, index) => new TrackBox(index, element.Footprint,
                    PadFeet(settings, element, p => p.LeftMm), PadFeet(settings, element, p => p.RightMm),
                    PadFeet(settings, element, p => p.BottomMm), PadFeet(settings, element, p => p.TopMm)))
                .ToList();

            // Frozen elements get keys past the participants' range so the engine can name a clash.
            var frozenBoxes = frozen
                .Select((element, index) => new TrackBox(participants.Count + index, element.Footprint))
                .ToList();

            var result = AlignEngine.Plan(boxes, frozenBoxes, Options(settings, usable));

            var moves = new List<AlignMove>();
            foreach (var placement in result.Placements)
            {
                var element = participants[placement.Key];
                moves.Add(new AlignMove
                {
                    Element = element,
                    DeltaX = placement.Target.MinX - element.Footprint.MinX,
                    DeltaY = placement.Target.MinY - element.Footprint.MinY,
                    Target = placement.Target,
                    Row = placement.Row,
                    Column = placement.Column,
                    Status = placement.Status,
                    Note = placement.Note,
                    Pad = PadFor(settings, element),
                    CanTick = true,
                    IsTicked = true
                });
            }

            moves.AddRange(FrozenMoves(frozen, settings, unticked, skipReason));

            string blocker = null;
            if (result.Overlap is (int a, int b))
                blocker = "Nothing was moved: "
                    + $"{NameOf(a, participants, frozen)} and {NameOf(b, participants, frozen)} would end up overlapping. "
                    + "Raise the minimum gap, or untick one of them.";

            var ordered = moves
                .OrderBy(m => m.Row ?? int.MaxValue)
                .ThenBy(m => m.Column ?? int.MaxValue)
                .ThenBy(m => m.Element.Footprint.MinX)
                .ToList();

            return new AlignPlan
            {
                Snapshot = snapshot,
                Usable = usable,
                Detected = detected,
                Moves = ordered,
                RowCount = result.RowCount,
                ColumnCount = result.ColumnCount,
                Notes = result.Notes,
                Blocker = blocker,
                Signature = SignatureOf(ordered, result.RowCount, result.ColumnCount, blocker)
            };
        }

        /// <summary>Measures the sheet's own margins and gaps, for the window's "Detected" labels.</summary>
        public static DetectedLayout Detect(SheetSnapshot snapshot, AlignDistributeSettings settings)
        {
            if (snapshot?.TitleBlockFrame is not LayoutRect frame)
                return new DetectedLayout { HasValues = false };

            var footprints = snapshot.Elements
                .Where(e => Included(e, settings))
                .Select(e => e.Footprint)
                .ToList();

            return MarginDetector.Detect(frame, footprints,
                Units.MmToFeet(settings.RowToleranceMm),
                settings.RoundDetected ? settings.RoundToMm : 0);
        }

        // ── Participation rules ─────────────────────────────────────────────

        /// <summary>True when this kind of element is part of the run at all, before the user's ticks.</summary>
        private static bool Included(SheetElementSnapshot element, AlignDistributeSettings settings)
            => element.Kind != SheetElementKind.Schedule || settings.IncludeSchedules;

        /// <summary>Why this element is left alone, or null when it may be moved.</summary>
        private static string SkipReason(SheetElementSnapshot element, AlignDistributeSettings settings,
            IReadOnlySet<long> unticked)
        {
            if (!Included(element, settings))
                return "Schedules are not included — tick \"Include schedules\" to align them too.";
            if (element.LockReason != null)
                return element.LockReason;
            if (element.IsPinned && !settings.MovePinned)
                return "Pinned, and \"Move pinned\" is off.";
            if (unticked.Contains(element.Key))
                return "Not ticked.";
            return null;
        }

        private static IEnumerable<AlignMove> FrozenMoves(List<SheetElementSnapshot> frozen,
            AlignDistributeSettings settings, IReadOnlySet<long> unticked, Dictionary<long, string> skipReason)
            => frozen.Select(element => new AlignMove
            {
                Element = element,
                Target = element.Footprint,
                Status = AlignStatus.Skipped,
                Note = skipReason.TryGetValue(element.Key, out string reason) ? reason : null,
                Pad = PadFor(settings, element),
                // A view nobody else can edit, or a pinned one while "Move pinned" is off, is left
                // alone whatever the tick says — so the tick is disabled rather than quietly ignored.
                CanTick = element.LockReason == null && !(element.IsPinned && !settings.MovePinned)
                          && Included(element, settings),
                IsTicked = !unticked.Contains(element.Key)
            });

        // ── Per-view clearance ──────────────────────────────────────────────

        /// <summary>The clearance saved for this element, or null when it uses the sheet gap.</summary>
        public static ViewPadding PadFor(AlignDistributeSettings settings, SheetElementSnapshot element)
            => element?.Name != null && settings.Padding != null
               && settings.Padding.TryGetValue(element.Name, out var pad) && pad != null && !pad.IsEmpty
                ? pad : null;

        private static double PadFeet(AlignDistributeSettings settings, SheetElementSnapshot element,
            Func<ViewPadding, double?> side)
        {
            var pad = PadFor(settings, element);
            double? mm = pad == null ? null : side(pad);
            return mm.HasValue ? Units.MmToFeet(mm.Value) : 0;
        }

        // ── Plumbing ────────────────────────────────────────────────────────

        private static AlignEngineOptions Options(AlignDistributeSettings settings, LayoutRect usable) => new()
        {
            Usable = usable,
            RowTolerance = Units.MmToFeet(settings.RowToleranceMm),
            ColumnTolerance = Units.MmToFeet(settings.ColumnToleranceMm),
            AlignRows = settings.AlignRows,
            AlignColumns = settings.AlignColumns,
            DistributeHorizontal = settings.DistributeHorizontal,
            DistributeVertical = settings.DistributeVertical,
            MinGapHorizontal = Units.MmToFeet(settings.MinGapHorizontalMm),
            MinGapVertical = Units.MmToFeet(settings.MinGapVerticalMm),
            SnapTolerance = Units.MmToFeet(Math.Max(0, settings.SnapToleranceMm)),
            MaxMove = Units.MmToFeet(Math.Max(0, settings.MaxMoveMm)),
            ReportOverLimit = settings.OverLimit == OverLimitMode.Report,
            KeepInsideMargins = settings.KeepInsideMargins,
            StopOnOverlap = settings.StopOnOverlap
        };

        private static string NameOf(int key, List<SheetElementSnapshot> participants, List<SheetElementSnapshot> frozen)
        {
            if (key >= 0 && key < participants.Count) return participants[key].Name;
            int index = key - participants.Count;
            return index >= 0 && index < frozen.Count ? frozen[index].Name : "a view";
        }

        private static AlignPlan Stopped(SheetSnapshot snapshot, DetectedLayout detected, string blocker,
            List<SheetElementSnapshot> frozen = null, Dictionary<long, string> skipReason = null)
        {
            var moves = (frozen ?? snapshot.Elements)
                .Select(element => new AlignMove
                {
                    Element = element,
                    Target = element.Footprint,
                    Status = AlignStatus.Skipped,
                    Note = skipReason != null && skipReason.TryGetValue(element.Key, out string reason) ? reason : null,
                    CanTick = true,
                    IsTicked = true
                })
                .ToList();

            return new AlignPlan
            {
                Snapshot = snapshot,
                Detected = detected,
                Moves = moves,
                Blocker = blocker,
                Signature = "blocked:" + blocker
            };
        }

        /// <summary>
        /// A short string that changes whenever the plan would move anything differently. Apply
        /// re-reads the sheet, re-plans and compares, so a sheet someone changed while the window
        /// was open is never moved against a stale preview.
        /// </summary>
        private static string SignatureOf(List<AlignMove> moves, int rows, int columns, string blocker)
        {
            var text = new StringBuilder();
            text.Append(rows).Append('x').Append(columns).Append('|').Append(blocker).Append('|');
            foreach (var move in moves.OrderBy(m => m.Key))
            {
                // Rounded to a micron so floating-point noise never invalidates a good preview.
                text.Append(move.Key).Append(':')
                    .Append(Math.Round(move.DeltaX, 6).ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Math.Round(move.DeltaY, 6).ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                    .Append((int)move.Status).Append(';');
            }
            return text.ToString();
        }
    }
}
