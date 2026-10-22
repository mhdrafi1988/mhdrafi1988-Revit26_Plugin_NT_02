namespace Revit26_Plugin_Revised.CreateSections.V012.Models
{
    /// <summary>
    /// V008: new. Persisted user options, per suite convention
    /// (%AppData%\Revit26_Plugin_Revised\CreateSectionsFromDetailLines\settings.json).
    /// Deliberately excludes SectionType/Template (ElementId references —
    /// not safely portable across documents/sessions) and SaveFolder for
    /// log export (stored in the same settings file, see SaveFolder below).
    /// </summary>
    public class SectionCreationSettings
    {
        public string SectionPrefix { get; set; } = "Zone_00_Section";

        public double FarClipMm { get; set; } = 10;
        public double SearchThresholdMm { get; set; } = 2000;
        public double TopPaddingMm { get; set; } = 1000;
        public double BottomPaddingMm { get; set; } = 1000;
        public double BottomOffsetMm { get; set; } = 10;
        public int ViewScale { get; set; } = 100;

        public bool SearchFloorHost { get; set; } = true;
        public bool SearchRoofHost { get; set; } = true;
        public bool SearchFloorLinked { get; set; } = true;
        public bool SearchRoofLinked { get; set; } = true;

        public bool UseThresholdFallback { get; set; } = true;

        public bool OpenAllAfterCreate { get; set; } = false;
        public bool DeleteLinesAfterCreate { get; set; } = false;

        // ================= VIEW CROP (V009 — new) =================
        // Defaults confirmed: all 3 toggles ON, offsets 100mm on all sides.

        public bool CropBoxActive { get; set; } = true;
        public bool CropRegionVisible { get; set; } = true;
        public bool AnnotationCropActive { get; set; } = true;

        public double TopAnnotationOffsetMm { get; set; } = 100;
        public double BottomAnnotationOffsetMm { get; set; } = 100;
        public double LeftAnnotationOffsetMm { get; set; } = 100;
        public double RightAnnotationOffsetMm { get; set; } = 100;

        /// <summary>
        /// Folder chosen for log .txt export. Asked once per session by the
        /// dialog and persisted here so subsequent sessions default to the
        /// same folder (per "ask save folder once/session, reuse" convention —
        /// this makes it reused across sessions too, unless you'd prefer
        /// session-only, which I've flagged below).
        /// </summary>
        public string LogSaveFolder { get; set; }
    }
}
