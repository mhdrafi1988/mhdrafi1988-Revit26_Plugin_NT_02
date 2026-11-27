using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>The level/offset parameter pair one category uses.</summary>
    public readonly struct LevelPair
    {
        /// <summary>Creates a pair.</summary>
        public LevelPair(BuiltInParameter level, BuiltInParameter offset, string source)
        {
            Level = level;
            Offset = offset;
            Source = source;
        }

        /// <summary>The parameter holding the level.</summary>
        public BuiltInParameter Level { get; }

        /// <summary>The parameter holding the offset from that level.</summary>
        public BuiltInParameter Offset { get; }

        /// <summary>Human-readable pair name, for example "Base Constraint + Base Offset".</summary>
        public string Source { get; }
    }

    /// <summary>Where one element's base sits, and whether that placement can be changed.</summary>
    public sealed class BasePlacement
    {
        /// <summary>True when a level/offset pair was found.</summary>
        public bool Found { get; set; }

        /// <summary>Level element id value.</summary>
        public long LevelId { get; set; }

        /// <summary>Level name.</summary>
        public string LevelName { get; set; } = string.Empty;

        /// <summary>Level elevation in millimetres.</summary>
        public double LevelElevationMm { get; set; }

        /// <summary>Offset from that level in millimetres.</summary>
        public double OffsetMm { get; set; }

        /// <summary>The element's real base elevation in millimetres.</summary>
        public double ElevationMm => LevelElevationMm + OffsetMm;

        /// <summary>Which pair was read.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The pair that was read, so a re-host writes the same parameters.</summary>
        public LevelPair Pair { get; set; }

        /// <summary>Either parameter of the pair cannot be written.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Nothing found.</summary>
        public static BasePlacement None => new() { Found = false };
    }

    /// <summary>
    /// Reads the base level and offset of a roof, floor or wall from the parameter pair its category uses:
    /// walls = Base Constraint + Base Offset; floors = Level + Height Offset From Level;
    /// footprint roofs = Base Level + Base Offset From Level; extrusion roofs = Reference Level + Offset.
    /// The first pair whose level parameter holds a level wins. Runs on the Revit API thread.
    /// </summary>
    public static class LevelPairReader
    {
        private static readonly LevelPair[] Pairs =
        {
            new(BuiltInParameter.WALL_BASE_CONSTRAINT, BuiltInParameter.WALL_BASE_OFFSET, "Base Constraint + Base Offset"),
            new(BuiltInParameter.ROOF_BASE_LEVEL_PARAM, BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM, "Base Level + Base Offset From Level"),
            new(BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM, BuiltInParameter.ROOF_CONSTRAINT_OFFSET_PARAM, "Reference Level + Offset"),
            new(BuiltInParameter.LEVEL_PARAM, BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, "Level + Height Offset From Level"),
        };

        /// <summary>Reads where <paramref name="e"/> sits; <see cref="BasePlacement.Found"/> is false when it has no such pair.</summary>
        public static BasePlacement Read(Document doc, Element e)
        {
            if (doc == null || e == null) return BasePlacement.None;

            foreach (LevelPair pair in Pairs)
            {
                Parameter levelParam = e.get_Parameter(pair.Level);
                if (levelParam == null || levelParam.StorageType != StorageType.ElementId) continue;
                if (doc.GetElement(levelParam.AsElementId()) is not Level level) continue;

                Parameter offsetParam = e.get_Parameter(pair.Offset);
                double offsetMm = offsetParam != null && offsetParam.StorageType == StorageType.Double
                    ? UnitsMm.FromFeet(offsetParam.AsDouble())
                    : 0;

                return new BasePlacement
                {
                    Found = true,
                    LevelId = level.Id.Value,
                    LevelName = level.Name,
                    LevelElevationMm = UnitsMm.FromFeet(level.Elevation),
                    OffsetMm = offsetMm,
                    Source = offsetParam == null ? pair.Source + " (no offset parameter, 0 used)" : pair.Source,
                    Pair = pair,
                    ReadOnly = levelParam.IsReadOnly || offsetParam == null || offsetParam.IsReadOnly
                };
            }
            return BasePlacement.None;
        }

        /// <summary>
        /// True when <paramref name="e"/> is a floor or roof whose shape editing is switched on, so changing its
        /// Level could reset its point edits.
        /// </summary>
        public static bool IsShapeEdited(Element e)
        {
            try
            {
                SlabShapeEditor editor = e switch
                {
                    Floor floor => floor.GetSlabShapeEditor(),
                    RoofBase roof => roof.GetSlabShapeEditor(),
                    _ => null
                };
                return editor != null && editor.IsValidObject && editor.IsEnabled;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Some roof kinds have no shape editor at all; treat that as "not shape-edited".
                return false;
            }
        }
    }
}
