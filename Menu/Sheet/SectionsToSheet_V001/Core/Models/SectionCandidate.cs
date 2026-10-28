namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models
{
    /// <summary>
    /// One section view that can go on the new sheet, read once so planning and the preview never
    /// touch the Revit API. Keys are element id values.
    /// </summary>
    public sealed class SectionCandidate
    {
        /// <summary>The section view's element id value.</summary>
        public long Key { get; init; }

        /// <summary>View name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Scale denominator (50 for 1:50), or 0 when unknown.</summary>
        public int Scale { get; init; }

        /// <summary>"1:50", or "—" when unknown.</summary>
        public string ScaleText => Scale > 0 ? "1:" + Scale : "—";

        /// <summary>Marker position along the source view's right direction (feet), or null when unknown.</summary>
        public double? MarkerAcross { get; init; }

        /// <summary>Marker position along the source view's up direction (feet), or null when unknown.</summary>
        public double? MarkerUp { get; init; }

        /// <summary>True when the marker is shown in the source view.</summary>
        public bool InSourceView { get; init; }

        /// <summary>The sheet the view is on now (element id value), or 0.</summary>
        public long CurrentSheetKey { get; init; }

        /// <summary>That sheet's number, or null.</summary>
        public string CurrentSheetNumber { get; init; }

        /// <summary>That sheet's name, or null.</summary>
        public string CurrentSheetName { get; init; }

        /// <summary>
        /// Why the view cannot be moved (owned by another user, changed in central), or null.
        /// </summary>
        public string LockReason { get; init; }

        /// <summary>True when the view is on a sheet now.</summary>
        public bool IsOnSheet => CurrentSheetKey != 0;
    }

    /// <summary>
    /// A view's footprint on a sheet: the viewport box united with its title, in feet, plus where
    /// the box centre sits relative to the footprint's bottom-left corner (Revit places a viewport
    /// by its box centre).
    /// </summary>
    public sealed class Footprint
    {
        /// <summary>Footprint width.</summary>
        public double Width { get; init; }

        /// <summary>Footprint height.</summary>
        public double Height { get; init; }

        /// <summary>Box centre X minus footprint left edge.</summary>
        public double CenterOffsetX { get; init; }

        /// <summary>Box centre Y minus footprint bottom edge.</summary>
        public double CenterOffsetY { get; init; }
    }

    /// <summary>What is on a sheet that one of the sections is on now.</summary>
    public sealed class SheetContent
    {
        /// <summary>The sheet's element id value.</summary>
        public long Key { get; init; }

        /// <summary>Sheet number.</summary>
        public string Number { get; init; } = string.Empty;

        /// <summary>Sheet name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Viewports on the sheet, all views.</summary>
        public int ViewportCount { get; init; }

        /// <summary>Schedules on the sheet (title block revision schedules not counted).</summary>
        public int ScheduleCount { get; init; }

        /// <summary>Why the sheet cannot be deleted (owned by another user, changed in central), or null.</summary>
        public string LockReason { get; init; }
    }
}
