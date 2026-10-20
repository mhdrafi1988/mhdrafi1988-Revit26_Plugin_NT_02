// =======================================================
// File: PipelineSettings.cs
// Location: Core/Models/
// Every option of the six steps. Saved as JSON to
// %AppData%\Revit26_Plugin\RoofPointsAndSlope\settings.json
// (Shared/Services/SettingsService). SchemaVersion lets a later version
// read and migrate this file. Defaults follow the tools each step comes
// from (Inner Loop Divider V012, Inner Loops + Perpendicular V007,
// Multiple Points V003, Ridge By Openings V070, Auto Slope By Drain V013).
// =======================================================

using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using System;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>Which edges a step works on. Both a location and an edge type must be ticked.</summary>
    public sealed class EdgeScope
    {
        /// <summary>Outer boundary edges.</summary>
        public bool Boundary { get; set; } = true;

        /// <summary>Opening edges.</summary>
        public bool Openings { get; set; }

        /// <summary>Straight edges.</summary>
        public bool Lines { get; set; } = true;

        /// <summary>Curved edges (arcs, ellipses, splines).</summary>
        public bool Curves { get; set; } = true;

        /// <summary>True when <paramref name="edge"/> is in this scope.</summary>
        public bool Includes(RoofEdge edge) =>
            edge != null &&
            (edge.IsBoundary ? Boundary : Openings) &&
            (edge.IsLine ? Lines : Curves);

        /// <summary>A copy, so a run never reads the window's live object.</summary>
        public EdgeScope Clone() => (EdgeScope)MemberwiseClone();
    }

    /// <summary>All options of the six steps.</summary>
    public sealed class PipelineSettings
    {
        /// <summary>File format version of this settings file.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Settings file format version.</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        // ── Whole run ────────────────────────────────────────────────────────
        /// <summary>
        /// Remove every shape point and split line on the roof before step 1 (Revit's Reset
        /// Shape — also removes points made by other tools). Off by default.
        /// </summary>
        public bool ClearExistingFirst { get; set; }

        /// <summary>
        /// Steps 2–4 skip a point closer than this (mm) to another point, so Revit isn't given
        /// thin triangles. Step 1 is exempt (its count is the user's choice). 0 = off.
        /// </summary>
        public double MinPointSpacingMm { get; set; } = 50;

        // ── Step 1: points around openings ──────────────────────────────────
        /// <summary>Run step 1.</summary>
        public bool InnerPointsEnabled { get; set; } = true;

        /// <summary>Count (points per opening) or Spacing (max gap).</summary>
        public DivisionMode InnerMode { get; set; } = DivisionMode.Count;

        /// <summary>Default points around a circular opening.</summary>
        public int CircularPoints { get; set; } = 6;

        /// <summary>Default points around a rectangular or other opening.</summary>
        public int CorneredPoints { get; set; } = 8;

        /// <summary>Spacing mode: largest gap between points along an opening (m).</summary>
        public double InnerSpacingMeters { get; set; } = 0.30;

        // ── Step 2: perpendicular points ─────────────────────────────────────
        /// <summary>Run step 2.</summary>
        public bool PerpendicularEnabled { get; set; } = true;

        /// <summary>Opening points closer than this (mm) are treated as one group.</summary>
        public double GroupProximityMm { get; set; } = 500;

        // ── Step 3: midpoint / quarter points ────────────────────────────────
        /// <summary>Run step 3.</summary>
        public bool MidQuarterEnabled { get; set; } = true;

        /// <summary>Add a point at half the edge length.</summary>
        public bool AddMidpoint { get; set; } = true;

        /// <summary>Add points at a quarter and three quarters of the edge length.</summary>
        public bool AddQuarterPoints { get; set; } = true;

        /// <summary>Edges step 3 works on.</summary>
        public EdgeScope MidQuarterScope { get; set; } = new EdgeScope();

        // ── Step 4: gap fill ─────────────────────────────────────────────────
        /// <summary>Run step 4.</summary>
        public bool GapFillEnabled { get; set; } = true;

        /// <summary>Largest allowed gap between points along an edge (m).</summary>
        public double GapIntervalMeters { get; set; } = 2.0;

        /// <summary>Edges step 4 works on.</summary>
        public EdgeScope GapScope { get; set; } = new EdgeScope();

        // ── Step 5: ridge between drains (Ridge By Openings V070 engine) ─────
        /// <summary>Run step 5.</summary>
        public bool RidgeEnabled { get; set; } = true;

        /// <summary>Drains closer than this (mm) act as one drain for the ridge.</summary>
        public double RidgeProximityMm { get; set; } = 50;

        /// <summary>Allowed difference (mm) between a ridge point's distances to its drains.</summary>
        public double RidgeValidationToleranceMm { get; set; } = 50;

        /// <summary>Add shape points and split lines along the ridges.</summary>
        public bool RidgeShapePoints { get; set; } = true;

        /// <summary>Draw the ridges as detail lines in the active view.</summary>
        public bool RidgeDetailLines { get; set; } = true;

        /// <summary>Line style of the ridge detail lines (empty = the first available).</summary>
        public string RidgeLineStyle { get; set; } = string.Empty;

        // ── Step 6: slope by drain (Auto Slope By Drain V013 engine) ─────────
        /// <summary>Run step 6.</summary>
        public bool SlopeEnabled { get; set; } = true;

        /// <summary>Slope (%).</summary>
        public double SlopePercent { get; set; } = 1.5;

        /// <summary>Max distance between two points for them to be linked (m).</summary>
        public double MaxEdgeMeters { get; set; } = 30;

        /// <summary>Max path length from a point to its drain (m); farther points are left at 0.</summary>
        public double MaxPathMeters { get; set; } = 40;

        /// <summary>Samples per path when checking it stays on the roof.</summary>
        public int PathSamples { get; set; } = 10;

        /// <summary>Insert points where paths cross curved edges, for accurate distances.</summary>
        public bool InsertCurveIntersectionPoints { get; set; }

        /// <summary>Write the Auto Slope By Drain Excel report.</summary>
        public bool ExportToExcel { get; set; }

        /// <summary>Folder for the Excel report.</summary>
        public string ExportFolder { get; set; } = string.Empty;

        /// <summary>Brings an older or partial file up to date (missing sections get defaults, values are clamped).</summary>
        public PipelineSettings Normalize()
        {
            MidQuarterScope ??= new EdgeScope();
            GapScope ??= new EdgeScope();
            ExportFolder ??= string.Empty;
            RidgeLineStyle ??= string.Empty;
            CircularPoints = Math.Max(1, CircularPoints);
            CorneredPoints = Math.Max(1, CorneredPoints);
            PathSamples = Math.Max(2, PathSamples);
            MinPointSpacingMm = Math.Max(0, MinPointSpacingMm);
            SchemaVersion = CurrentSchemaVersion;
            return this;
        }

        /// <summary>A deep copy, so a run never reads the window's live object.</summary>
        public PipelineSettings Clone()
        {
            var copy = (PipelineSettings)MemberwiseClone();
            copy.MidQuarterScope = MidQuarterScope?.Clone() ?? new EdgeScope();
            copy.GapScope = GapScope?.Clone() ?? new EdgeScope();
            return copy;
        }
    }
}
