using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTag.V001.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.IO;

namespace Revit26_Plugin_Revised.FloorTag.V001
{
    /// <summary>
    /// V015: FaceRef method only.
    /// Places tag on floor face reference (best quality, equivalent to manual click).
    /// Tag WILL DELETE if slab shape is edited (face regenerates).
    /// No fallback — hard fail if FaceRef is invalid.
    /// </summary>
    public static class FloorTaggingService_FaceRef
    {
        private const double DuplicateTolFt = 10.0 / 304.8;

        private static readonly string LogFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FloorTag_V001_Log.txt");

        public static LogEntry PlaceTag(
            Document     doc,
            Reference    faceRef,
            Floor     floor,
            XYZ          origin,
            FloorTagViewModel vm)
        {
            if (doc == null || floor == null || origin == null || vm == null)
                return Fail(origin, "Null argument passed to PlaceTag.");

            View view = doc.ActiveView;
            if (view == null)
                return Fail(origin, "No active view.");

            // ── Check for existing tag within 10 mm ──────────────────
            if (DuplicateExistsInView(doc, view, origin))
            {
                var entry = new LogEntry(LogLevel.Warning,
                    $"Skipped (duplicate within 10 mm) — {Fmt(origin)}");
                WriteFile(entry);
                return entry;
            }

            // ── FaceRef only — no fallback ─────────────────────────────
            if (faceRef == null)
                return Fail(origin, "No face reference available.");

            // ── Compute leader geometry ────────────────────────────────
            double bendFt = UnitUtils.ConvertToInternalUnits(vm.BendOffset, UnitTypeId.Millimeters);
            double endFt  = UnitUtils.ConvertToInternalUnits(vm.EndOffset,  UnitTypeId.Millimeters);

            var mode = vm.BendInward
                ? FloorTagGeometryHelper.PlacementMode.Inward
                : FloorTagGeometryHelper.PlacementMode.Outward;

            var (bend, end) = FloorTagGeometryHelper.ComputeTwoStepLeaderPlacement(
                view, floor, origin, bendFt, endFt, mode);

            // ── Try FaceRef ────────────────────────────────────────────
            if (TryPlace(doc, view, faceRef, origin, bend, end, vm))
            {
                var entry = new LogEntry(LogLevel.Success,
                    $"FaceRef — tag placed at {Fmt(origin)}");
                WriteFile(entry);
                return entry;
            }

            // ── Hard fail ──────────────────────────────────────────────
            return Fail(origin, "FaceRef placement failed — no fallback.");
        }

        private static bool TryPlace(
            Document         doc,
            View             view,
            Reference        reference,
            XYZ              origin,
            XYZ              bend,
            XYZ              end,
            FloorTagViewModel vm)
        {
            try
            {
                SpotDimension tag = doc.Create.NewSpotElevation(
                    view,
                    reference,
                    origin,
                    bend,
                    end,
                    origin,
                    vm.UseLeader);

                if (tag == null) return false;

                if (vm.SelectedSpotTagType?.TagType != null)
                    tag.ChangeTypeId(vm.SelectedSpotTagType.TagType.Id);

                return true;
            }
            catch
            {
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
                if (el is SpotDimension sd)
                {
                    try
                    {
                        XYZ pos = sd.Origin;
                        if (pos != null && pos.DistanceTo(origin) <= DuplicateTolFt)
                            return true;
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FloorTaggingService_FaceRef] DuplicateExistsInView: could not read SpotDimension {el.Id} origin — {ex.Message}");
                    }
                }
            }
            return false;
        }

        private static LogEntry Fail(XYZ origin, string reason)
        {
            string msg = origin != null
                ? $"ERROR at {Fmt(origin)} — {reason}"
                : $"ERROR — {reason}";

            var entry = new LogEntry(LogLevel.Error, msg);
            WriteFile(entry);
            return entry;
        }

        private static void WriteFile(LogEntry entry)
        {
            try
            {
                File.AppendAllText(
                    LogFilePath,
                    $"{entry}{Environment.NewLine}");
            }
            catch { }
        }

        private static string Fmt(XYZ p) =>
            $"({p.X:0.000}, {p.Y:0.000}, {p.Z:0.000})";
    }
}
