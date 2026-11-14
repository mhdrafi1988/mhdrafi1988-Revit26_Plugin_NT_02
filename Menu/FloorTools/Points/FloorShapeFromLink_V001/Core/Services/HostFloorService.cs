using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Services
{
    /// <summary>Reads floors and their shape-editing points from the host model.</summary>
    public static class HostFloorService
    {
        /// <summary>
        /// Keeps only the floors among <paramref name="ids"/>.
        /// <paramref name="ignored"/> counts everything else (extrusion floors, other categories, deleted ids).
        /// </summary>
        public static List<Floor> FilterFloors(Document doc, IEnumerable<ElementId> ids, out int ignored)
        {
            var floors = new List<Floor>();
            ignored = 0;
            foreach (ElementId id in ids)
            {
                if (doc.GetElement(id) is Floor floor) floors.Add(floor);
                else ignored++;
            }
            return floors;
        }

        /// <summary>True for plan views (floor, structural and ceiling plans) that are not view templates.</summary>
        public static bool IsPlanView(View view) =>
            view is ViewPlan plan && !plan.IsTemplate &&
            (plan.ViewType == ViewType.FloorPlan || plan.ViewType == ViewType.EngineeringPlan || plan.ViewType == ViewType.CeilingPlan);

        /// <summary>Label shown in the grid: "Type name [id]".</summary>
        public static string Label(Element floor) => $"{floor.Name} [{floor.Id.Value}]";

        /// <summary>
        /// Reads the shape-editing points of <paramref name="floor"/>. Returns an empty list when shape
        /// editing is off (no points exist yet); <paramref name="reason"/> then says why.
        /// </summary>
        public static List<HostPoint> ReadPoints(Floor floor, out string reason)
        {
            reason = null;
            SlabShapeEditor editor = floor.GetSlabShapeEditor();
            if (editor == null)
            {
                reason = "this floor type does not support shape editing";
                return new List<HostPoint>();
            }
            if (!editor.IsEnabled)
            {
                reason = "shape editing is not on (the floor has no shape-editing points)";
                return new List<HostPoint>();
            }

            string label = Label(floor);
            HostFloor floorBase = ReadBase(floor);
            var points = new List<HostPoint>();
            int index = 0;
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            {
                if (vertex?.Position == null) continue;
                points.Add(new HostPoint { FloorId = floor.Id, FloorLabel = label, Index = ++index, Position = vertex.Position, Base = floorBase });
            }
            if (points.Count == 0) reason = "the floor has no shape-editing points";
            return points;
        }

        /// <summary>
        /// Reads the base surface elevation of <paramref name="floor"/> (its level elevation plus Height
        /// Offset From Level, which for a floor is its TOP surface) and the total thickness of its type. A value that cannot be read
        /// becomes 0 and is explained in <see cref="HostFloor.Note"/>.
        /// </summary>
        public static HostFloor ReadBase(Floor floor)
        {
            Document doc = floor.Document;
            var notes = new List<string>();

            double baseZ = 0;
            // Floor port: LEVEL_PARAM / FLOOR_HEIGHTABOVELEVEL_PARAM replace the roof's
            // ROOF_BASE_LEVEL_PARAM / ROOF_LEVEL_OFFSET_PARAM.
            ElementId levelId = floor.get_Parameter(BuiltInParameter.LEVEL_PARAM)?.AsElementId() ?? floor.LevelId;
            if (doc.GetElement(levelId) is Level level)
                baseZ = level.Elevation + (floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0);
            else
                notes.Add("base level not found, base elevation taken as 0");

            double thickness = 0;
            FloorType type = floor.FloorType;
            double? width = type?.GetCompoundStructure()?.GetWidth();
            if (width.HasValue && width.Value > 0) thickness = width.Value;
            else
            {
                double? fallback = type?.get_Parameter(BuiltInParameter.FLOOR_ATTR_DEFAULT_THICKNESS_PARAM)?.AsDouble();
                if (fallback.HasValue && fallback.Value > 0) thickness = fallback.Value;
                else notes.Add("floor thickness not found, taken as 0");
            }

            return new HostFloor { BaseZFt = baseZ, ThicknessFt = thickness, Note = string.Join("; ", notes) };
        }

        /// <summary>
        /// Checks that <paramref name="floor"/> can be edited: not deleted and not owned by another user.
        /// Returns null when it can, otherwise the reason.
        /// </summary>
        public static string EditBlocker(Document doc, Element floor)
        {
            if (floor == null || !floor.IsValidObject) return "the floor no longer exists";
            if (doc.IsWorkshared)
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, floor.Id);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return $"the floor is checked out by {WorksharingUtils.GetWorksharingTooltipInfo(doc, floor.Id).Owner}";
            }
            return null;
        }
    }
}
