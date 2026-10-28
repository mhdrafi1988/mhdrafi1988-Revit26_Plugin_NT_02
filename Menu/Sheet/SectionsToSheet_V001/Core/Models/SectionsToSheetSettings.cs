using System;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models
{
    /// <summary>
    /// Persisted to %AppData%\Revit26_Plugin_Revised\SectionsToSheet\settings.json.
    /// Lengths are millimetres on paper; margins are measured inward from the title block's edge.
    /// </summary>
    public sealed class SectionsToSheetSettings
    {
        /// <summary>Current settings-file format version.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Format version of the file this was read from.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Title block for new sheets, as "Family : Type"; empty = the first one in the project.</summary>
        public string TitleBlockName { get; set; } = string.Empty;

        /// <summary>Viewport type for the placed views; empty = the project's default viewport type.</summary>
        public string ViewportTypeName { get; set; } = string.Empty;

        /// <summary>Number of the last sheet this tool created; the next suggestion follows it.</summary>
        public string LastSheetNumber { get; set; } = string.Empty;

        /// <summary>Name for new sheets.</summary>
        public string SheetName { get; set; } = "SECTIONS";

        /// <summary>Open the first new sheet when done.</summary>
        public bool OpenSheetWhenDone { get; set; } = true;

        /// <summary>Grid or Rows.</summary>
        public ArrangeMode Mode { get; set; } = ArrangeMode.Grid;

        /// <summary>Placement order.</summary>
        public SectionOrder Order { get; set; } = SectionOrder.Plan;

        /// <summary>Where the rows sit vertically.</summary>
        public VerticalMode Vertical { get; set; } = VerticalMode.Top;

        /// <summary>Short last row (Rows only).</summary>
        public LastRowMode LastRow { get; set; } = LastRowMode.Left;

        /// <summary>What to do when the views do not fit on one sheet.</summary>
        public OverflowMode Overflow { get; set; } = OverflowMode.AddSheets;

        /// <summary>Take picked sections off the sheet they are on now and move them to the new sheet.</summary>
        public bool RemoveFromOldSheets { get; set; } = true;

        /// <summary>Delete old sheets that end up with no views and no schedules.</summary>
        public bool DeleteEmptySheets { get; set; }

        /// <summary>Top margin (mm).</summary>
        public double MarginTopMm { get; set; } = 30;

        /// <summary>Bottom margin (mm).</summary>
        public double MarginBottomMm { get; set; } = 150;

        /// <summary>Left margin (mm).</summary>
        public double MarginLeftMm { get; set; } = 30;

        /// <summary>Right margin (mm) — typically clears the title strip.</summary>
        public double MarginRightMm { get; set; } = 200;

        /// <summary>Smallest gap between views side by side (mm).</summary>
        public double MinHorizontalGapMm { get; set; } = 20;

        /// <summary>Smallest gap between rows (mm).</summary>
        public double MinVerticalGapMm { get; set; } = 20;

        /// <summary>Plan order: markers whose heights differ by less than this on the printed view share a row (mm).</summary>
        public double RowToleranceMm { get; set; } = 10;

        /// <summary>
        /// Brings a file from any version (or a hand-edited one) to valid values: unknown enum
        /// values and negative or non-numeric lengths fall back to the defaults.
        /// </summary>
        public SectionsToSheetSettings Normalize()
        {
            var d = new SectionsToSheetSettings();
            TitleBlockName ??= string.Empty;
            ViewportTypeName ??= string.Empty;
            LastSheetNumber ??= string.Empty;
            if (string.IsNullOrWhiteSpace(SheetName)) SheetName = d.SheetName;
            if (!Enum.IsDefined(typeof(ArrangeMode), Mode)) Mode = d.Mode;
            if (!Enum.IsDefined(typeof(SectionOrder), Order)) Order = d.Order;
            if (!Enum.IsDefined(typeof(VerticalMode), Vertical)) Vertical = d.Vertical;
            if (!Enum.IsDefined(typeof(LastRowMode), LastRow)) LastRow = d.LastRow;
            if (!Enum.IsDefined(typeof(OverflowMode), Overflow)) Overflow = d.Overflow;
            MarginTopMm = Length(MarginTopMm, d.MarginTopMm);
            MarginBottomMm = Length(MarginBottomMm, d.MarginBottomMm);
            MarginLeftMm = Length(MarginLeftMm, d.MarginLeftMm);
            MarginRightMm = Length(MarginRightMm, d.MarginRightMm);
            MinHorizontalGapMm = Length(MinHorizontalGapMm, d.MinHorizontalGapMm);
            MinVerticalGapMm = Length(MinVerticalGapMm, d.MinVerticalGapMm);
            RowToleranceMm = Length(RowToleranceMm, d.RowToleranceMm);
            Version = CurrentVersion;
            return this;
        }

        private static double Length(double value, double fallback) => double.IsFinite(value) && value >= 0 ? value : fallback;
    }
}
