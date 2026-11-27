using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>What a pair's offset is measured from, before read-only state decides the tier.</summary>
    public enum OffsetBasis
    {
        /// <summary>There is no offset to measure.</summary>
        None = 0,

        /// <summary>The offset is measured from the level, so <c>Z = level elevation + offset</c> holds.</summary>
        FromLevel,

        /// <summary>The offset is measured from the host face, so it says nothing about the elevation.</summary>
        FromHost
    }

    /// <summary>The level/offset parameter pair one kind of element uses, and what its offset is measured from.</summary>
    public readonly struct LevelPair
    {
        /// <summary>Creates a pair. <paramref name="offsets"/> are tried in order; the first that exists wins.</summary>
        public LevelPair(BuiltInParameter level, BuiltInParameter[] offsets, OffsetBasis basis, string source)
        {
            Level = level;
            Offsets = offsets;
            Basis = basis;
            Source = source;
        }

        /// <summary>The parameter holding the level.</summary>
        public BuiltInParameter Level { get; }

        /// <summary>Candidate offset parameters, in order of preference. Empty for a level with no offset.</summary>
        public BuiltInParameter[] Offsets { get; }

        /// <summary>What the offset is measured from.</summary>
        public OffsetBasis Basis { get; }

        /// <summary>Human-readable pair name, for example "Base Constraint + Base Offset".</summary>
        public string Source { get; }
    }

    /// <summary>Where one element's base sits, and whether that placement can be changed.</summary>
    public sealed class BasePlacement
    {
        /// <summary>True when a level was found, whatever the tier.</summary>
        public bool Found { get; set; }

        /// <summary>Whether the offset is measured from that level, from the host, or missing.</summary>
        public LevelTier Tier { get; set; } = LevelTier.None;

        /// <summary>Level element id value.</summary>
        public long LevelId { get; set; }

        /// <summary>Level name.</summary>
        public string LevelName { get; set; } = string.Empty;

        /// <summary>Level elevation in millimetres.</summary>
        public double LevelElevationMm { get; set; }

        /// <summary>
        /// Offset from that level in millimetres. Meaningful only when
        /// <see cref="Tier"/> is <see cref="LevelTier.LevelAndOffset"/>.
        /// </summary>
        public double OffsetMm { get; set; }

        /// <summary>
        /// True when <c>level elevation + offset</c> is the element's real base elevation, so the
        /// nearest-level arithmetic holds.
        /// </summary>
        public bool OffsetKnown => LevelTiers.WritesOffsets(Tier);

        /// <summary>The element's base elevation in millimetres; only meaningful when <see cref="OffsetKnown"/>.</summary>
        public double ElevationMm => LevelElevationMm + OffsetMm;

        /// <summary>Which pair was read.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>The pair that was read, so a re-host writes the same parameters.</summary>
        public LevelPair Pair { get; set; }

        /// <summary>Either parameter of the pair cannot be written.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Nothing found.</summary>
        public static BasePlacement None => new() { Found = false, Tier = LevelTier.None };
    }

    /// <summary>
    /// Reads the level and offset of any level-based model element, and says which tier the result is in.
    /// <para>
    /// <b>Tier 1</b> (<see cref="LevelTier.LevelAndOffset"/>) — the offset is measured from the level, so
    /// <c>Z = level elevation + offset</c> holds and the element can be re-hosted: walls, floors, ceilings,
    /// roofs, structural columns and framing, MEP curves, stairs, and families placed on a level.
    /// </para><para>
    /// <b>Tier 2</b> (<see cref="LevelTier.HostOffset"/>) — the level is readable but "Offset from Host" is
    /// measured from the host face, not the level, so no elevation is computed: doors, windows and
    /// face-hosted families.
    /// </para><para>
    /// <b>Tier 3</b> (<see cref="LevelTier.ScheduleOnly"/>) — only a read-only Schedule Level, with no offset.
    /// </para>
    /// The element's own <c>LevelId</c> is consulted first, as <c>TrackerId</c> does; the parameter table
    /// below is the fallback and is what fixes the tier. Runs on the Revit API thread.
    /// </summary>
    public static class LevelPairReader
    {
        // Order matters. A family instance offers both "Elevation from Level" (tier 1) and "Offset from
        // Host" (tier 2), so the tier-1 pair must be tried first.
        // The general table, for everything that is not a family instance. Order matters.
        private static readonly LevelPair[] Pairs =
        {
            new(BuiltInParameter.WALL_BASE_CONSTRAINT, new[] { BuiltInParameter.WALL_BASE_OFFSET },
                OffsetBasis.FromLevel, "Base Constraint + Base Offset"),

            new(BuiltInParameter.ROOF_BASE_LEVEL_PARAM, new[] { BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM },
                OffsetBasis.FromLevel, "Base Level + Base Offset From Level"),

            new(BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM, new[] { BuiltInParameter.ROOF_CONSTRAINT_OFFSET_PARAM },
                OffsetBasis.FromLevel, "Reference Level + Offset"),

            new(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, new[] { BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM },
                OffsetBasis.FromLevel, "Base Level + Base Offset"),

            new(BuiltInParameter.RBS_START_LEVEL_PARAM, new[] { BuiltInParameter.RBS_OFFSET_PARAM },
                OffsetBasis.FromLevel, "Reference Level + Offset"),

            new(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, new[] { BuiltInParameter.STAIRS_BASE_OFFSET },
                OffsetBasis.FromLevel, "Base Level + Base Offset"),

            // Floors and ceilings share LEVEL_PARAM; their offset parameters differ.
            new(BuiltInParameter.LEVEL_PARAM,
                new[] { BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM, BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM },
                OffsetBasis.FromLevel, "Level + Height Offset From Level"),

            new(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                OffsetBasis.FromLevel, "Reference Level + Elevation from Level"),

            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                OffsetBasis.FromLevel, "Level + Elevation from Level"),

            // "Offset from Host" is measured from the host face, not from the level.
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM },
                OffsetBasis.FromHost, "Level + Offset from Host"),

            // A level and nothing to measure from it.
            new(BuiltInParameter.SCHEDULE_LEVEL_PARAM, new BuiltInParameter[0],
                OffsetBasis.None, "Schedule Level"),

            new(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, new BuiltInParameter[0],
                OffsetBasis.None, "Schedule Level"),
        };

        // ── family placement tables (1.2.0) ───────────────────────────────────
        // A family instance is classified by how it is placed, which the family itself states, rather than
        // by guessing from whichever parameters happen to exist.

        // Placed on one level: its Elevation from Level is measured from that level.
        private static readonly LevelPair[] OneLevel =
        {
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                OffsetBasis.FromLevel, "Level + Elevation from Level"),
            new(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                OffsetBasis.FromLevel, "Reference Level + Elevation from Level"),
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM },
                OffsetBasis.FromHost, "Level + Offset from Host"),
        };

        // Placed between two levels, as a structural column is: its base pair is the one that matters here.
        private static readonly LevelPair[] TwoLevels =
        {
            new(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, new[] { BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM },
                OffsetBasis.FromLevel, "Base Level + Base Offset"),
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                OffsetBasis.FromLevel, "Level + Elevation from Level"),
        };

        // Hosted on a level and in a host, as a door or window is. Sill Height IS measured from the level,
        // so these get real offsets — but their Level follows the host and is read-only, which puts them in
        // Tier 2 rather than Tier 1.
        private static readonly LevelPair[] OneLevelHosted =
        {
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM },
                OffsetBasis.FromLevel, "Level + Sill Height"),
            new(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM },
                OffsetBasis.FromLevel, "Reference Level + Sill Height"),
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_ELEVATION_PARAM },
                OffsetBasis.FromLevel, "Level + Elevation from Level"),
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM },
                OffsetBasis.FromHost, "Level + Offset from Host"),
            new(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, new BuiltInParameter[0],
                OffsetBasis.None, "Schedule Level"),
        };

        // Placed on a work plane: its level is informational only.
        private static readonly LevelPair[] WorkPlane =
        {
            new(BuiltInParameter.SCHEDULE_LEVEL_PARAM, new BuiltInParameter[0],
                OffsetBasis.None, "Schedule Level"),
            new(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, new BuiltInParameter[0],
                OffsetBasis.None, "Schedule Level"),
            new(BuiltInParameter.FAMILY_LEVEL_PARAM, new BuiltInParameter[0],
                OffsetBasis.None, "Level (work-plane-based, no offset from it)"),
        };

        /// <summary>
        /// The pairs to try for <paramref name="e"/>, in order. A family instance is classified by its
        /// family's <see cref="FamilyPlacementType"/>; anything else uses the general table (1.2.0).
        /// </summary>
        private static LevelPair[] PairsFor(Element e)
        {
            FamilyPlacementType? placement = PlacementOf(e);
            if (placement == null) return Pairs;

            return placement switch
            {
                FamilyPlacementType.OneLevelBased => OneLevel,
                FamilyPlacementType.TwoLevelsBased => TwoLevels,
                FamilyPlacementType.OneLevelBasedHosted => OneLevelHosted,
                FamilyPlacementType.WorkPlaneBased => WorkPlane,
                FamilyPlacementType.CurveBased or FamilyPlacementType.CurveDrivenStructural => OneLevel,
                _ => Pairs
            };
        }

        /// <summary>How <paramref name="e"/>'s family is placed, or null when it is not a family instance.</summary>
        public static FamilyPlacementType? PlacementOf(Element e)
        {
            try
            {
                return (e as FamilyInstance)?.Symbol?.Family?.FamilyPlacementType;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        /// <summary>True when <paramref name="e"/> belongs to a family modelled in place.</summary>
        public static bool IsInPlace(Element e)
        {
            try
            {
                return (e as FamilyInstance)?.Symbol?.Family?.IsInPlace == true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads where <paramref name="e"/> sits. <see cref="BasePlacement.Found"/> is false when it has no
        /// level at all; <see cref="BasePlacement.Tier"/> says how far the result can be trusted.
        /// </summary>
        public static BasePlacement Read(Document doc, Element e)
        {
            if (doc == null || e == null) return BasePlacement.None;

            foreach (LevelPair pair in PairsFor(e))
            {
                Parameter levelParam = e.get_Parameter(pair.Level);
                if (levelParam == null || levelParam.StorageType != StorageType.ElementId) continue;
                if (doc.GetElement(levelParam.AsElementId()) is not Level level) continue;
                return Build(level, levelParam, e, pair);
            }

            // No known pair, but the element still reports a level (for example a hosted element whose
            // category the table does not cover). Report it as schedule-only: readable, never re-hosted.
            if (doc.GetElement(e.LevelId) is Level own)
            {
                return new BasePlacement
                {
                    Found = true,
                    Tier = LevelTier.ScheduleOnly,
                    LevelId = own.Id.Value,
                    LevelName = own.Name,
                    LevelElevationMm = UnitsMm.FromFeet(own.Elevation),
                    Source = "Element level (no offset parameter)",
                    ReadOnly = true
                };
            }
            return BasePlacement.None;
        }

        // The tier is derived here, not stored in the table: it is the pair's basis combined with whether
        // the two parameters can actually be written on this element. That is what separates a door
        // (offset from its level, but a read-only level) from a wall (both writable).
        private static BasePlacement Build(Level level, Parameter levelParam, Element e, LevelPair pair)
        {
            var placement = new BasePlacement
            {
                Found = true,
                LevelId = level.Id.Value,
                LevelName = level.Name,
                LevelElevationMm = UnitsMm.FromFeet(level.Elevation),
                Source = pair.Source,
                Pair = pair
            };

            if (pair.Basis == OffsetBasis.None)
            {
                placement.Tier = LevelTier.ScheduleOnly;
                placement.ReadOnly = true;
                return placement;
            }

            Parameter offsetParam = FirstOffset(e, pair.Offsets);
            if (offsetParam == null)
            {
                // The level parameter matched but its offset does not exist on this element, so the
                // elevation cannot be worked out. Report the level only rather than assume zero.
                placement.Tier = LevelTier.ScheduleOnly;
                placement.Source = pair.Source + " (no offset parameter on this element)";
                placement.ReadOnly = true;
                return placement;
            }

            placement.ReadOnly = levelParam.IsReadOnly || offsetParam.IsReadOnly;

            if (pair.Basis == OffsetBasis.FromHost)
            {
                placement.Tier = LevelTier.HostOffset;
                return placement;
            }

            // Measured from the level, so the arithmetic holds either way; only re-host depends on whether
            // the pair can be written.
            placement.OffsetMm = UnitsMm.FromFeet(offsetParam.AsDouble());
            placement.Tier = placement.ReadOnly ? LevelTier.LevelAndOffsetLocked : LevelTier.LevelAndOffset;
            return placement;
        }

        /// <summary>
        /// The level and offset parameters behind <paramref name="placement"/> on <paramref name="e"/>, for a
        /// re-host that must write the same pair that was read. Both are null unless the placement is Tier 1.
        /// </summary>
        public static void ParametersOf(Element e, BasePlacement placement, out Parameter level, out Parameter offset)
        {
            level = null;
            offset = null;
            if (e == null || placement == null || !placement.OffsetKnown || placement.ReadOnly) return;
            level = e.get_Parameter(placement.Pair.Level);
            offset = FirstOffset(e, placement.Pair.Offsets);
        }

        private static Parameter FirstOffset(Element e, IReadOnlyList<BuiltInParameter> candidates)
        {
            foreach (BuiltInParameter id in candidates)
            {
                Parameter p = e.get_Parameter(id);
                if (p != null && p.StorageType == StorageType.Double) return p;
            }
            return null;
        }

        /// <summary>
        /// True when <paramref name="e"/> is a floor or roof whose shape editing is switched on, so changing
        /// its Level could reset its point edits.
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
