namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>How the created detail lines are packaged.</summary>
    public enum GroupingMode
    {
        /// <summary>Everything in one detail group EPS2D_&lt;floorId&gt;_&lt;viewId&gt; (V001 behaviour).</summary>
        SingleGroup,

        /// <summary>
        /// One detail group per layer (…_Grid, …_HipRidge, …_Contours, …_Labels) so each layer can be moved,
        /// rotated or aligned on its own. In "each edge" mode the grid is split per edge (…_Grid_E1, …_Grid_E2 …).
        /// </summary>
        GroupPerLayer,

        /// <summary>
        /// One detail group per side of the floor (…_E1, …_E2 …): that side's grid lines, hatches and labels.
        /// Hips/ridges and contours get their own groups. Lets each side be moved or aligned on its own.
        /// </summary>
        GroupPerSide,

        /// <summary>Loose detail lines — every line can be aligned, rotated or edited directly. Cannot be replaced automatically.</summary>
        NoGroup
    }
}
