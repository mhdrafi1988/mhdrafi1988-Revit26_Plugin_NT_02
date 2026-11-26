using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models
{
    /// <summary>
    /// One view's own clearance, in mm, on each of its four sides. Added to the minimum gap around
    /// that view, so a crowded detail keeps its breathing room when a row is evened out.
    /// A null side means "use the sheet gap".
    /// </summary>
    public sealed class ViewPadding
    {
        /// <summary>Extra clearance above the view (mm), or null for the sheet gap.</summary>
        public double? TopMm { get; set; }

        /// <summary>Extra clearance below the view (mm), or null for the sheet gap.</summary>
        public double? BottomMm { get; set; }

        /// <summary>Extra clearance left of the view (mm), or null for the sheet gap.</summary>
        public double? LeftMm { get; set; }

        /// <summary>Extra clearance right of the view (mm), or null for the sheet gap.</summary>
        public double? RightMm { get; set; }

        /// <summary>True when every side is null, i.e. this view uses the sheet gap all round.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsEmpty => TopMm == null && BottomMm == null && LeftMm == null && RightMm == null;

        /// <summary>A copy, so the view model can edit without touching the saved settings.</summary>
        public ViewPadding Clone() => new()
        {
            TopMm = TopMm, BottomMm = BottomMm, LeftMm = LeftMm, RightMm = RightMm
        };
    }

    /// <summary>
    /// Persisted to %AppData%\Revit26_Plugin_Revised\SheetAlignDistribute\settings.json.
    /// Every default is the smallest possible change to a sheet: rows share a bottom edge, columns
    /// share a left edge, and the gaps are left alone.
    /// </summary>
    public sealed class AlignDistributeSettings
    {
        /// <summary>
        /// Current settings-file format version. Version 1 is the first release; a file written by a
        /// later version is read as far as this version understands it, and unknown fields take
        /// their defaults (CLAUDE.md, Backward compatibility 4).
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>Format version of the file this was read from.</summary>
        public int Version { get; set; } = CurrentVersion;

        // ── Margins ────────────────────────────────────────────────────────

        /// <summary>Whether the margins are measured off the sheet or typed by the user.</summary>
        public MarginSource MarginSource { get; set; } = MarginSource.Detected;

        /// <summary>Top margin inside the title block (mm).</summary>
        public double MarginTopMm { get; set; } = 10;

        /// <summary>Bottom margin inside the title block (mm).</summary>
        public double MarginBottomMm { get; set; } = 20;

        /// <summary>Left margin inside the title block (mm).</summary>
        public double MarginLeftMm { get; set; } = 20;

        /// <summary>Right margin inside the title block (mm) — typically clears the title strip.</summary>
        public double MarginRightMm { get; set; } = 180;

        /// <summary>Round detected margins and gaps to <see cref="RoundToMm"/>, so they read as whole numbers.</summary>
        public bool RoundDetected { get; set; } = true;

        /// <summary>Step the detected margins and gaps are rounded to (mm). 0 turns rounding off.</summary>
        public double RoundToMm { get; set; } = 5;

        // ── Grid detection ─────────────────────────────────────────────────

        /// <summary>How far apart two views' alignment edges may be and still count as one row (mm).</summary>
        public double RowToleranceMm { get; set; } = 25;

        /// <summary>How far apart two views' alignment edges may be and still count as one column (mm).</summary>
        public double ColumnToleranceMm { get; set; } = 25;

        /// <summary>
        /// What a view's footprint is measured as. Box only (title ignored) by default since 1.2.0
        /// (owner request, 2026-11-26); a saved settings file keeps the choice it holds.
        /// </summary>
        public FootprintMode Footprint { get; set; } = FootprintMode.BoxOnly;

        /// <summary>Include schedules placed on the sheet. Off by default (Rafi, 2026-11-17).</summary>
        public bool IncludeSchedules { get; set; }

        // ── Align and distribute ───────────────────────────────────────────

        /// <summary>The shared horizontal line each row snaps to.</summary>
        public RowAlign AlignRows { get; set; } = RowAlign.Bottom;

        /// <summary>The shared vertical line each column snaps to.</summary>
        public ColumnAlign AlignColumns { get; set; } = ColumnAlign.Left;

        /// <summary>What happens to the gaps across a row.</summary>
        public DistributeMode DistributeHorizontal { get; set; } = DistributeMode.Off;

        /// <summary>What happens to the gaps between rows.</summary>
        public DistributeMode DistributeVertical { get; set; } = DistributeMode.Off;

        /// <summary>Smallest gap allowed between two views side by side (mm).</summary>
        public double MinGapHorizontalMm { get; set; } = 20;

        /// <summary>Smallest gap allowed between two rows (mm).</summary>
        public double MinGapVerticalMm { get; set; } = 20;

        // ── Movement limits ────────────────────────────────────────────────

        /// <summary>A view already this close to its line is left untouched (mm).</summary>
        public double SnapToleranceMm { get; set; } = 2;

        /// <summary>A view is never moved further than this (mm). 0 or less turns the limit off.</summary>
        public double MaxMoveMm { get; set; } = 25;

        /// <summary>What to do with a view over <see cref="MaxMoveMm"/>.</summary>
        public OverLimitMode OverLimit { get; set; } = OverLimitMode.Report;

        // ── Safety ─────────────────────────────────────────────────────────

        /// <summary>Move pinned views too; they are pinned again afterwards.</summary>
        public bool MovePinned { get; set; } = true;

        /// <summary>Never let an aligned view cross the margins; one that would is left where it is.</summary>
        public bool KeepInsideMargins { get; set; } = true;

        /// <summary>Apply nothing if any two views would end up overlapping.</summary>
        public bool StopOnOverlap { get; set; } = true;

        // ── Per-view clearance ─────────────────────────────────────────────

        /// <summary>
        /// Per-view clearance, keyed by view name so it survives a re-read of the sheet and applies
        /// to the same detail on the next sheet that carries it. Views with no entry use the sheet gap.
        /// </summary>
        public Dictionary<string, ViewPadding> Padding { get; set; } = new();

        /// <summary>
        /// A shallow copy for one test plan or one Apply with different options. The per-view
        /// clearance dictionary is shared and must not be edited through the copy.
        /// </summary>
        public AlignDistributeSettings Clone() => (AlignDistributeSettings)MemberwiseClone();
    }
}
