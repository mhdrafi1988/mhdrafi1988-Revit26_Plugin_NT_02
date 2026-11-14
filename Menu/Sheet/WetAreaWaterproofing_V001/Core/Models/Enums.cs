namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models
{
    /// <summary>What a run does with rooms that already have waterproofing from an earlier run.</summary>
    public enum ReRunMode
    {
        /// <summary>Replace the old roof with a new one for the same room.</summary>
        UpdateInPlace = 0
    }

    /// <summary>What to do when no slab is found under a room.</summary>
    public enum NoSlabMode
    {
        /// <summary>Place the roof at the room's level plus the offset.</summary>
        LevelPlusOffset = 0
    }

    /// <summary>State of one room in the grid and in the report.</summary>
    public enum RoomStatus
    {
        /// <summary>Clean outline, nothing to report.</summary>
        Ready,
        /// <summary>The outline needed a repair and is now closed.</summary>
        Repaired,
        /// <summary>The outline's area differs from the room area by more than the tolerance.</summary>
        AreaWarning,
        /// <summary>The outline is an approximation (hull), not the real boundary.</summary>
        Approximate,
        /// <summary>No slab under the room; the level plus offset is used.</summary>
        NoSlab,
        /// <summary>The room or its slab is checked out by another user.</summary>
        Owned,
        /// <summary>The room is not placed or not enclosed.</summary>
        NotEnclosed,
        /// <summary>A roof from an earlier run exists for this room.</summary>
        AlreadyDone,
        /// <summary>No closed outline could be built.</summary>
        Failed
    }
}
