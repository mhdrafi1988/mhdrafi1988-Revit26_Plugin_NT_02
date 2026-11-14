using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Infrastructure
{
    /// <summary>Base level and base offset of one element, as read by <see cref="BaseLevelReader"/>.</summary>
    public readonly struct BaseLevelInfo
    {
        /// <summary>Creates a result.</summary>
        public BaseLevelInfo(bool found, string levelName, double offsetMm, string source)
        {
            Found = found;
            LevelName = levelName;
            OffsetMm = offsetMm;
            Source = source;
        }

        /// <summary>True when a base level was found.</summary>
        public bool Found { get; }

        /// <summary>Level name.</summary>
        public string LevelName { get; }

        /// <summary>Offset in millimetres.</summary>
        public double OffsetMm { get; }

        /// <summary>Which parameters were used, for example "Base Constraint + Base Offset".</summary>
        public string Source { get; }

        /// <summary>No base level.</summary>
        public static BaseLevelInfo None => new(false, string.Empty, 0, string.Empty);
    }

    /// <summary>
    /// Reads an element's base level and base offset from the parameter pair its category uses:
    /// walls = Base Constraint + Base Offset; columns = Base Level + Base Offset;
    /// floors = Level + Height Offset From Level; roofs = Base Level + Base Offset From Level
    /// (or, for extrusion roofs, Reference Level + Offset); MEP curves = Reference Level + Offset;
    /// families = Level or Schedule Level + Offset from Host (or Elevation from Level).
    /// The first pair whose level parameter holds a level wins. A level with no offset parameter
    /// gives offset 0.
    /// </summary>
    public static class BaseLevelReader
    {
        private static readonly (BuiltInParameter Level, BuiltInParameter[] Offsets, string Source)[] Pairs =
        {
            (BuiltInParameter.WALL_BASE_CONSTRAINT, new[] { BuiltInParameter.WALL_BASE_OFFSET }, "Base Constraint + Base Offset"),
            (BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, new[] { BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM }, "Base Level + Base Offset"),
            (BuiltInParameter.ROOF_BASE_LEVEL_PARAM, new[] { BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM }, "Base Level + Base Offset From Level"),
            (BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM, new[] { BuiltInParameter.ROOF_CONSTRAINT_OFFSET_PARAM }, "Reference Level + Offset"),
            (BuiltInParameter.RBS_START_LEVEL_PARAM, new[] { BuiltInParameter.RBS_OFFSET_PARAM }, "Reference Level + Offset"),
            (BuiltInParameter.LEVEL_PARAM, new[] { BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM }, "Level + Height Offset From Level"),
            (BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM }, "Level + Offset from Host"),
            (BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM }, "Schedule Level + Offset from Host"),
        };

        /// <summary>Reads the base level and offset of <paramref name="e"/>; <see cref="BaseLevelInfo.Found"/> is false when it has none.</summary>
        public static BaseLevelInfo Read(Document doc, Element e)
        {
            foreach (var pair in Pairs)
            {
                Parameter levelParam = e.get_Parameter(pair.Level);
                if (levelParam == null || levelParam.StorageType != StorageType.ElementId) continue;
                if (doc.GetElement(levelParam.AsElementId()) is not Level level) continue;

                foreach (BuiltInParameter offsetId in pair.Offsets)
                {
                    Parameter offset = e.get_Parameter(offsetId);
                    if (offset != null && offset.StorageType == StorageType.Double)
                        return new BaseLevelInfo(true, level.Name, ToMm(offset.AsDouble()), pair.Source);
                }
                return new BaseLevelInfo(true, level.Name, 0, pair.Source + " (no offset parameter, 0 used)");
            }
            return BaseLevelInfo.None;
        }

        private static double ToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    }
}
