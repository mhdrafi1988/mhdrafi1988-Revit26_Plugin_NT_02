using System.Collections.Generic;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Models
{
    /// <summary>
    /// Plain POCO persisted via System.Text.Json to
    /// %AppData%\Revit26_Plugin_Revised\LinkedDetailLineGenerator\settings.json
    /// (no version segment in path, per suite convention).
    ///
    /// Loaded on window open, saved on window close and after a successful Run.
    /// Does NOT persist live Revit references (ElementId/link instance handles) —
    /// only display names/ids and user-configured values, since links can be
    /// reloaded/removed between sessions. LastSelectedLinkInstanceIds is best-effort:
    /// re-matched by instance name on load, not guaranteed to resolve.
    /// </summary>
    public class ToolSettings
    {
        public List<string> LastSelectedLinkInstanceNames { get; set; } = new();

        public CircleMarkerSettingsDto CircleMarker { get; set; } = new();
        public RectangleMarkerSettingsDto RectangleMarker { get; set; } = new();
        public ActualProfileSettingsDto ActualProfile { get; set; } = new();

        public ProcessingScopeDto ProcessingScope { get; set; } = new();
        public ComplexCurveSettingsDto ComplexCurve { get; set; } = new();
        public GlobalOverrideSettingsDto GlobalOverride { get; set; } = new();

        /// <summary>Folder chosen for manual log export — asked once per session, reused.</summary>
        public string? LastLogExportFolder { get; set; }
    }

    // ── DTOs mirror the ObservableObject models but stay plain for clean JSON round-trip ──

    public class CircleMarkerSettingsDto
    {
        public double DiameterMm { get; set; } = 300;
    }

    public class RectangleMarkerSettingsDto
    {
        public double WidthMm { get; set; } = 300;
        public double HeightMm { get; set; } = 300;

        /// <summary>Stored as its enum name (e.g. "InstanceRotation"), same
        /// convention as ComplexCurveSettingsDto.FallbackShape, so the JSON stays
        /// human-readable and tolerant of enum member reordering.</summary>
        public string AlignmentMode { get; set; } = "InstanceRotation";
        public double ManualAngleDegrees { get; set; } = 0;
    }

    public class ActualProfileSettingsDto
    {
        /// <summary>Stored as its enum name ("Circle" / "Rectangle").</summary>
        public string FallbackShape { get; set; } = "Circle";
    }

    public class ProcessingScopeDto
    {
        public bool LimitToActiveView { get; set; } = true;
        public bool TrimToBoundary { get; set; } = true;
        public LoopClosingSettingsDto OuterLoopClosing { get; set; } = new();
        public LoopClosingSettingsDto InnerLoopClosing { get; set; } = new();
        public bool RemoveEngulfedOnly { get; set; } = false;
        public bool MergePartialOverlaps { get; set; } = false;
        public bool JoinCollinearLines { get; set; } = false;
        public double LineJoinToleranceMm { get; set; } = 1.0;
    }

    public class LoopClosingSettingsDto
    {
        public bool CloseOpenLoops { get; set; } = false;
        public bool CapOpenEnds { get; set; } = false;
    }

    public class ComplexCurveSettingsDto
    {
        public bool ReplaceWithFallback { get; set; } = false;
        public string FallbackShape { get; set; } = "StraightChord";
    }

    public class GlobalOverrideSettingsDto
    {
        public bool IsEnabled { get; set; } = false;
        public string LineStyleName { get; set; } = string.Empty;
        public string ColorName { get; set; } = "None";
    }
}
