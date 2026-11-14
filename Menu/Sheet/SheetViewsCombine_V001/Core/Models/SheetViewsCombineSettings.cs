using Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout;
using System;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Models
{
    /// <summary>
    /// Persisted to %AppData%\Revit26_Plugin_Revised\SheetViewsCombine\settings.json.
    /// Lengths are millimetres on paper; margins are measured inward from the title block's edge.
    /// </summary>
    public sealed class SheetViewsCombineSettings
    {
        /// <summary>Current settings-file format version.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Format version of the file this was read from.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>Where the ticked views go.</summary>
        public TargetMode Target { get; set; } = TargetMode.Sheet1;

        /// <summary>What happens to unticked views already on the target.</summary>
        public UntickedTargetMode Unticked { get; set; } = UntickedTargetMode.KeepInPlace;

        /// <summary>What happens when one view cannot be moved.</summary>
        public FailureMode OnFailure { get; set; } = FailureMode.SkipAndReport;

        /// <summary>Packing strategy.</summary>
        public PackStrategy Strategy { get; set; } = PackStrategy.Rows;

        /// <summary>Order of the views going into the packing.</summary>
        public GroupOrder Order { get; set; } = GroupOrder.SheetThenType;

        /// <summary>Keep each moved view's viewport type (title style).</summary>
        public bool KeepViewportType { get; set; } = true;

        /// <summary>Keep a moved view's detail number when it is free on the target.</summary>
        public bool KeepDetailNumbers { get; set; } = true;

        /// <summary>Open the target sheet when done.</summary>
        public bool OpenSheetWhenDone { get; set; } = true;

        /// <summary>Name for a new target sheet.</summary>
        public string NewSheetName { get; set; } = "COMBINED VIEWS";

        /// <summary>Top margin (mm).</summary>
        public double MarginTopMm { get; set; } = 15;

        /// <summary>Bottom margin (mm).</summary>
        public double MarginBottomMm { get; set; } = 15;

        /// <summary>Left margin (mm).</summary>
        public double MarginLeftMm { get; set; } = 15;

        /// <summary>Right margin (mm) — typically clears the title strip.</summary>
        public double MarginRightMm { get; set; } = 130;

        /// <summary>Smallest gap between views side by side (mm).</summary>
        public double HorizontalGapMm { get; set; } = 15;

        /// <summary>Smallest gap between rows (mm).</summary>
        public double VerticalGapMm { get; set; } = 15;

        /// <summary>
        /// Brings a file from any version (or a hand-edited one) to valid values: unknown enum
        /// values and negative or non-numeric lengths fall back to the defaults.
        /// </summary>
        public SheetViewsCombineSettings Normalize()
        {
            var d = new SheetViewsCombineSettings();
            if (!Enum.IsDefined(typeof(TargetMode), Target)) Target = d.Target;
            if (!Enum.IsDefined(typeof(UntickedTargetMode), Unticked)) Unticked = d.Unticked;
            if (!Enum.IsDefined(typeof(FailureMode), OnFailure)) OnFailure = d.OnFailure;
            if (!Enum.IsDefined(typeof(PackStrategy), Strategy)) Strategy = d.Strategy;
            if (!Enum.IsDefined(typeof(GroupOrder), Order)) Order = d.Order;
            if (string.IsNullOrWhiteSpace(NewSheetName)) NewSheetName = d.NewSheetName;
            MarginTopMm = Length(MarginTopMm, d.MarginTopMm);
            MarginBottomMm = Length(MarginBottomMm, d.MarginBottomMm);
            MarginLeftMm = Length(MarginLeftMm, d.MarginLeftMm);
            MarginRightMm = Length(MarginRightMm, d.MarginRightMm);
            HorizontalGapMm = Length(HorizontalGapMm, d.HorizontalGapMm);
            VerticalGapMm = Length(VerticalGapMm, d.VerticalGapMm);
            Version = CurrentVersion;
            return this;
        }

        /// <summary>A copy, so a run is not affected by later edits in the window.</summary>
        public SheetViewsCombineSettings Clone() => (SheetViewsCombineSettings)MemberwiseClone();

        private static double Length(double value, double fallback) => double.IsFinite(value) && value >= 0 && value < 5000 ? value : fallback;
    }
}
