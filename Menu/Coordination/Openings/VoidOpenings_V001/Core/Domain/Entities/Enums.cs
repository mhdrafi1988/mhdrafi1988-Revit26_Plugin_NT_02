namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>Structural categories the tool can check.</summary>
    public enum StructuralCategory
    {
        /// <summary>Floors and slabs.</summary>
        Floor,
        /// <summary>Walls.</summary>
        Wall,
        /// <summary>Beams and braces.</summary>
        StructuralFraming,
        /// <summary>Structural columns.</summary>
        StructuralColumn,
        /// <summary>Foundations.</summary>
        StructuralFoundation,
        /// <summary>Stairs, including runs and landings.</summary>
        Stair,
        /// <summary>Ramps.</summary>
        Ramp,
        /// <summary>Generic models.</summary>
        GenericModel,
    }

    /// <summary>MEP categories the tool can check.</summary>
    public enum MepCategory
    {
        /// <summary>Pipes.</summary>
        Pipe,
        /// <summary>Pipe fittings.</summary>
        PipeFitting,
        /// <summary>Pipe accessories.</summary>
        PipeAccessory,
        /// <summary>Ducts.</summary>
        Duct,
        /// <summary>Duct fittings.</summary>
        DuctFitting,
        /// <summary>Duct accessories.</summary>
        DuctAccessory,
        /// <summary>Cable trays.</summary>
        CableTray,
        /// <summary>Cable tray fittings.</summary>
        CableTrayFitting,
        /// <summary>Conduits.</summary>
        Conduit,
        /// <summary>Conduit fittings.</summary>
        ConduitFitting,
    }

    /// <summary>Cross-section of an MEP element.</summary>
    public enum ProfileShape
    {
        /// <summary>Round: pipes, conduits, round ducts.</summary>
        Round,
        /// <summary>Rectangular: ducts, cable trays.</summary>
        Rectangular,
    }

    /// <summary>Shape of an opening.</summary>
    public enum OpeningShape
    {
        /// <summary>Round hole, sized by diameter.</summary>
        Circular,
        /// <summary>Rectangular hole, sized by width and height.</summary>
        Rectangular,
    }

    /// <summary>Where an opening is in the detect → generate → cut workflow.</summary>
    public enum OpeningStatus
    {
        /// <summary>Found by analysis, nothing placed yet.</summary>
        Detected,
        /// <summary>Found, but a person should check it before it is generated.</summary>
        Review,
        /// <summary>Rejected by the user.</summary>
        Rejected,
        /// <summary>Void instance placed; structure not cut (linked structure or unsupported category).</summary>
        Generated,
        /// <summary>Void placed and the cut verified.</summary>
        Cut,
        /// <summary>Placement or cutting failed.</summary>
        Failed,
    }

    /// <summary>How a detected opening relates to what earlier runs stored in the model.</summary>
    public enum TrackingState
    {
        /// <summary>No stored record for this clash.</summary>
        New,
        /// <summary>Stored record matches what was detected.</summary>
        Existing,
        /// <summary>Source geometry changed since the record was stored.</summary>
        Updated,
        /// <summary>The void instance was changed by hand after it was generated.</summary>
        ManuallyModified,
        /// <summary>The clash is gone: a source element was deleted or moved away.</summary>
        SourceDeleted,
    }
}
