using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models
{
    /// <summary>
    /// Packed layout result for a single view — final center point (feet, sheet
    /// space) and whether it fit within the resolved placeable region.
    ///
    /// V021 FIX: previously defined inside ReadingOrderPackingService.cs.
    /// When that file was deleted as dead code (ReadingOrderPackingService
    /// itself was confirmed unused since V009), this type was deleted along
    /// with it — but SheetOrderPackingService, RearrangeEngine, and
    /// SheetAutoRearrangeViewModel all still depend on it, causing
    /// "type or namespace could not be found" build errors. Moved here as
    /// its own model file, where it belongs regardless of which packing
    /// service produces it.
    /// </summary>
    public class PackedViewPlacement
    {
        public ViewOnSheetItem Item { get; set; } = null!;
        public XYZ NewCenter { get; set; } = XYZ.Zero;
        public bool Fits { get; set; }
    }
}
