namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models
{
    /// <summary>V029: the shared line the views of one row snap to. Off keeps the fill strategy's own positions.</summary>
    public enum GridRowAlign { Off, Top, Center, Bottom }

    /// <summary>V029: the shared line the views of one column snap to. Off keeps the fill strategy's own positions.</summary>
    public enum GridColumnAlign { Off, Left, Center, Right }

    /// <summary>
    /// V029: how rows / columns are spread across the usable area on one axis.
    /// Off keeps the fixed H / V gap. EdgeToEdge puts the first and last view on the
    /// area edges with equal gaps between. EqualSpacing makes the gaps between views
    /// and the gaps to the area edges all the same.
    /// </summary>
    public enum DistributeMode { Off, EdgeToEdge, EqualSpacing }

    /// <summary>
    /// V029: the Align &amp; Distribute options. Everything defaults to Off, which leaves
    /// the layout exactly as V028 produced it.
    /// </summary>
    public class AlignDistributeSettings
    {
        /// <summary>Row line: views in the same row share their top, centre or bottom.</summary>
        public GridRowAlign AlignRows { get; set; } = GridRowAlign.Off;

        /// <summary>Column line: views in the same column share their left, centre or right.</summary>
        public GridColumnAlign AlignColumns { get; set; } = GridColumnAlign.Off;

        /// <summary>Horizontal spread across the usable width.</summary>
        public DistributeMode DistributeH { get; set; } = DistributeMode.Off;

        /// <summary>Vertical spread across the usable height.</summary>
        public DistributeMode DistributeV { get; set; } = DistributeMode.Off;

        /// <summary>True when rows or columns are aligned. Distribute only works together with an alignment.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsActive => AlignRows != GridRowAlign.Off || AlignColumns != GridColumnAlign.Off;
    }
}
