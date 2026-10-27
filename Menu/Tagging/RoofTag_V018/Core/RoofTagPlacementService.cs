using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofTag.V018.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.IO;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>
    /// Places a spot-elevation tag on a roof face reference (best quality, equivalent to a manual click).
    /// The tag is deleted if the slab shape is edited later (the face regenerates).
    /// No fallback: placement fails if the face reference is invalid.
    /// </summary>
    internal static class RoofTagPlacementService
    {
        private const double DuplicateTolFt = 10.0 / 304.8;

        private static readonly string LogFilePath = Path.Combine(ToolGuard.LogFolder, "RoofTag.log");

        /// <summary>Places one tag at <paramref name="origin"/>; returns the log entry describing the result.</summary>
        public static LogEntry PlaceTag(Document doc, Reference faceRef, RoofBase roof, XYZ origin, RoofTagOptions options)
        {
            if (doc == null || roof == null || origin == null || options == null)
                return Fail(origin, "Null argument passed to PlaceTag.");

            View view = doc.ActiveView;
            if (view == null)
                return Fail(origin, "No active view.");

            if (DuplicateExistsInView(doc, view, origin))
            {
                var skipped = new LogEntry(LogLevel.Warning, $"Skipped (duplicate within 10 mm) — {Fmt(origin)}");
                WriteFile(skipped);
                return skipped;
            }

            if (faceRef == null)
                return Fail(origin, "No face reference available.");

            var mode = options.BendInward
                ? RoofTagGeometryHelper.PlacementMode.Inward
                : RoofTagGeometryHelper.PlacementMode.Outward;

            var (bend, end) = RoofTagGeometryHelper.ComputeTwoStepLeaderPlacement(
                view, roof, origin,
                MmLogFormatter.ToFeet(options.BendOffsetMm), MmLogFormatter.ToFeet(options.EndOffsetMm), mode);

            if (TryPlace(doc, view, faceRef, origin, bend, end, options, out string error))
            {
                var placed = new LogEntry(LogLevel.Success, $"FaceRef — tag placed at {Fmt(origin)}");
                WriteFile(placed);
                return placed;
            }

            return Fail(origin, $"FaceRef placement failed — no fallback. {error}");
        }

        private static bool TryPlace(Document doc, View view, Reference reference, XYZ origin, XYZ bend, XYZ end,
            RoofTagOptions options, out string error)
        {
            error = null;
            try
            {
                SpotDimension tag = doc.Create.NewSpotElevation(view, reference, origin, bend, end, origin, options.UseLeader);
                if (tag == null)
                {
                    error = "Revit returned no spot elevation.";
                    return false;
                }

                if (options.SpotTagTypeId != null)
                    tag.ChangeTypeId(options.SpotTagTypeId);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool DuplicateExistsInView(Document doc, View view, XYZ origin)
        {
            var existing = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(SpotDimension))
                .WhereElementIsNotElementType();

            foreach (Element el in existing)
            {
                if (el is not SpotDimension sd) continue;
                try
                {
                    XYZ pos = sd.Origin;
                    if (pos != null && pos.DistanceTo(origin) <= DuplicateTolFt)
                        return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RoofTagPlacementService] could not read SpotDimension {el.Id} origin — {ex.Message}");
                }
            }
            return false;
        }

        private static LogEntry Fail(XYZ origin, string reason)
        {
            string msg = origin != null ? $"ERROR at {Fmt(origin)} — {reason}" : $"ERROR — {reason}";
            var entry = new LogEntry(LogLevel.Error, msg);
            WriteFile(entry);
            return entry;
        }

        // The window log is the primary record; this file is a copy. A write failure must not stop the run.
        private static void WriteFile(LogEntry entry)
        {
            try
            {
                Directory.CreateDirectory(ToolGuard.LogFolder);
                File.AppendAllText(LogFilePath, $"{entry}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoofTagPlacementService] could not write {LogFilePath} — {ex.Message}");
            }
        }

        private static string Fmt(XYZ p) => $"({p.X:0.000}, {p.Y:0.000}, {p.Z:0.000})";
    }
}
