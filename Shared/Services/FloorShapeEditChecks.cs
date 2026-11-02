// =======================================================
// File: FloorShapeEditChecks.cs
// Namespace: Revit26_Plugin_Revised.Shared.Services
// Shared pre-run checks for the Floor Slope tools (By Drain, By Point):
// can this floor's shape points be edited, and does its type have a
// Variable layer (without one, shape editing tilts the whole slab).
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>What the user chose for a floor whose type has no Variable layer.</summary>
    public enum NoVariableLayerChoice
    {
        /// <summary>Slope the floor anyway (the whole slab follows the slope).</summary>
        Continue,

        /// <summary>Leave this floor unchanged.</summary>
        Skip
    }

    /// <summary>
    /// Pre-run checks shared by the Floor Slope tools. All methods are read-only
    /// and never throw for a null or deleted floor.
    /// </summary>
    public static class FloorShapeEditChecks
    {
        /// <summary>
        /// Returns why the floor's shape points cannot be edited, or null when they can.
        /// Checks: group membership, shape-editor support, read-only document,
        /// and (in workshared models) ownership by another user.
        /// </summary>
        /// <param name="floor">The floor to check.</param>
        public static string GetBlockReason(Floor floor)
        {
            if (floor == null || !floor.IsValidObject)
                return "the floor no longer exists";

            Document doc = floor.Document;
            if (doc.IsReadOnly)
                return "the model is read-only";

            if (floor.GroupId != null && floor.GroupId != ElementId.InvalidElementId)
                return "it is in a group (ungroup it first)";

            SlabShapeEditor editor;
            try
            {
                editor = floor.GetSlabShapeEditor();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                editor = null;
            }
            if (editor == null)
                return "this floor does not support shape editing";

            if (doc.IsWorkshared)
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, floor.Id);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return "it is borrowed by another user (ask them to relinquish it)";
            }

            return null;
        }

        /// <summary>
        /// True when the floor's type has a Variable layer, so shape editing only
        /// changes the top of the slab. False when it has none (or the type has no
        /// compound structure): the whole slab then follows the slope.
        /// </summary>
        /// <param name="floor">The floor to check.</param>
        public static bool HasVariableLayer(Floor floor)
        {
            if (floor == null || !floor.IsValidObject) return false;

            if (floor.Document.GetElement(floor.GetTypeId()) is not FloorType type)
                return false;

            CompoundStructure cs = type.GetCompoundStructure();
            return cs != null && cs.VariableLayerIndex >= 0;
        }

        /// <summary>
        /// Asks the user what to do with a floor whose type has no Variable layer.
        /// Closing the dialog counts as Skip, so nothing changes without a clear yes.
        /// </summary>
        /// <param name="floor">The floor the question is about.</param>
        /// <param name="title">Dialog title (the tool name).</param>
        /// <param name="skipText">Text of the "skip" choice, e.g. "Skip this floor" or "Cancel".</param>
        public static NoVariableLayerChoice AskNoVariableLayer(Floor floor, string title, string skipText = "Skip this floor")
        {
            string typeName = floor?.Document.GetElement(floor.GetTypeId())?.Name ?? "(unknown type)";
            var dialog = new TaskDialog(title)
            {
                MainInstruction = "This floor type has no Variable layer",
                MainContent =
                    $"Floor: {floor?.Name} (Id {floor?.Id.Value})\nType: {typeName}\n\n" +
                    "Without a Variable layer, shape editing tilts the whole slab: " +
                    "its bottom follows the slope as well as its top.\n\n" +
                    "To slope only the top, tick 'Variable' on a layer of the floor type, then run again.",
                CommonButtons = TaskDialogCommonButtons.None,
                AllowCancellation = true
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Continue", "Slope the whole slab.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, skipText, "Leave this floor unchanged.");

            return dialog.Show() == TaskDialogResult.CommandLink1
                ? NoVariableLayerChoice.Continue
                : NoVariableLayerChoice.Skip;
        }
    }
}
