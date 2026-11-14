using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using System;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run
{
    /// <summary>Works out what the grid shows for a room: its status, the reason and the slab text.</summary>
    public static class RoomStatusRules
    {
        /// <summary>
        /// Sets <see cref="RoomRow.Status"/>, <see cref="RoomRow.StatusDetail"/> and
        /// <see cref="RoomRow.SlabText"/> on the candidate's row. The most serious state wins:
        /// owned by someone else, no outline, approximate, area warning, repaired, no slab, already done.
        /// </summary>
        public static void Apply(RoomCandidate c)
        {
            RoomRow row = c?.Row;
            if (row == null) return;
            RoomOutline outline = c.Outline;
            row.HasExistingRoof = c.ExistingRoofIds != null && c.ExistingRoofIds.Count > 0;
            string done = !row.HasExistingRoof ? string.Empty
                : string.IsNullOrEmpty(c.ExistingRunId) ? " · already has a roof; a run replaces it"
                : $" · already has a roof (run {c.ExistingRunId}); a run replaces it";

            row.SlabText = row.SlabTopMm.HasValue
                ? "Floor · " + (row.SlabTopMm.Value / 1000).ToString("+0.000;-0.000", CultureInfo.InvariantCulture)
                : "No slab";

            if (!string.IsNullOrEmpty(row.Owner))
            {
                Set(row, RoomStatus.Owned, "Owned by " + row.Owner);
                return;
            }
            if (outline == null)
            {
                Set(row, RoomStatus.NotChecked, "Not a wet room: outline and slab are checked when it is run" + done);
                row.SlabText = string.Empty;
                return;
            }
            if (outline.Status == RoomStatus.NotEnclosed)
            {
                Set(row, RoomStatus.NotEnclosed, "Not placed or not enclosed");
                return;
            }

            string area = "Area " + outline.AreaDiffPct.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
            switch (outline.Status)
            {
                case RoomStatus.Failed:
                    RepairStep failed = outline.Steps.LastOrDefault(s => !s.Passed);
                    Set(row, RoomStatus.Failed, failed == null ? "No closed outline" : $"{failed.Name}: {failed.Detail}");
                    return;
                case RoomStatus.Approximate:
                    Set(row, RoomStatus.Approximate, "Approximate outline · " + area + done);
                    return;
                case RoomStatus.AreaWarning:
                    Set(row, RoomStatus.AreaWarning, area + done);
                    return;
                case RoomStatus.Repaired:
                    RepairStep passed = outline.Steps.LastOrDefault(s => s.Passed);
                    Set(row, RoomStatus.Repaired, (passed == null ? "Repaired" : $"{passed.Name}: {passed.Detail}") + done);
                    return;
            }

            if (!row.SlabTopMm.HasValue) Set(row, RoomStatus.NoSlab, "No slab under the room: level + offset is used" + done);
            else if (row.HasExistingRoof)
                Set(row, RoomStatus.AlreadyDone, string.IsNullOrEmpty(c.ExistingRunId)
                    ? "Already done; a run replaces the roof" : $"Already done (run {c.ExistingRunId}); a run replaces the roof");
            else Set(row, RoomStatus.Ready, string.Empty);
        }

        private static void Set(RoomRow row, RoomStatus status, string detail)
        {
            row.Status = status;
            row.StatusDetail = detail;
        }
    }
}
