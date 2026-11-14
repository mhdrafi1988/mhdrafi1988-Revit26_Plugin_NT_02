// =======================================================
// File: RoofChecks.cs
// Namespace: Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Infrastructure
// Read-only pre-run checks: can this roof's shape points be edited, and
// does its type have a Variable layer. Never throws.
// =======================================================
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Infrastructure
{
    /// <summary>Pre-run checks for one footprint roof. All methods are read-only.</summary>
    public static class RoofChecks
    {
        /// <summary>
        /// Why the roof cannot be sloped, or null when it can. Checks: still exists, read-only model,
        /// group membership, slope-defining edges, shape-editor support and (workshared models)
        /// ownership by another user.
        /// </summary>
        /// <param name="roof">The roof to check.</param>
        public static string GetBlockReason(FootPrintRoof roof)
        {
            try
            {
                if (roof == null || !roof.IsValidObject) return "the roof no longer exists";

                Document doc = roof.Document;
                if (doc.IsReadOnly) return "the model is read-only";
                if (doc.IsLinked) return "it is in a linked model";

                if (roof.GroupId != null && roof.GroupId != ElementId.InvalidElementId)
                    return "it is in a group (ungroup it first)";

                int slopeEdges = 0;
                foreach (ModelCurveArray arr in roof.GetProfiles())
                    foreach (ModelCurve mc in arr)
                        if (roof.get_DefinesSlope(mc)) slopeEdges++;
                if (slopeEdges > 0)
                    return slopeEdges + " sketch edge(s) define slope - shape editing needs a flat roof (untick 'Defines Slope')";

                if (roof.GetSlabShapeEditor() == null)
                    return "this roof does not support shape editing";

                if (doc.IsWorkshared &&
                    WorksharingUtils.GetCheckoutStatus(doc, roof.Id) == CheckoutStatus.OwnedByOtherUser)
                    return "it is borrowed by another user (ask them to relinquish it)";

                return null;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                return "it could not be checked: " + ex.Message;
            }
        }

        /// <summary>
        /// True when the roof's type has a Variable layer, so shape editing only changes the top.
        /// False when it has none: the whole roof then follows the slope.
        /// </summary>
        /// <param name="roof">The roof to check.</param>
        public static bool HasVariableLayer(FootPrintRoof roof)
        {
            try
            {
                if (roof == null || !roof.IsValidObject) return false;
                if (!(roof.Document.GetElement(roof.GetTypeId()) is RoofType type)) return false;
                CompoundStructure cs = type.GetCompoundStructure();
                return cs != null && cs.VariableLayerIndex >= 0;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
        }
    }
}
