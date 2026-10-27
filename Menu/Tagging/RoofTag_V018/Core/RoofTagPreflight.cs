using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.RoofTag.V018.Core
{
    /// <summary>Checks that must pass before the tool changes the model. Each returns a message, or null when fine.</summary>
    internal static class RoofTagPreflight
    {
        /// <summary>Checks the document is editable and the active view can hold spot elevations.</summary>
        public static string CheckSession(Document doc)
        {
            if (doc == null) return "Open a project first.";
            if (doc.IsReadOnly) return "The active document is read-only.";

            View view = doc.ActiveView;
            if (view == null) return "There is no active view.";
            if (view.IsTemplate || view is ViewSheet || view is ViewSchedule)
                return "Open a plan, section, elevation or 3D view. Spot elevations cannot be placed in this view.";

            return null;
        }

        /// <summary>
        /// Enables the roof's slab shape editor when it is off. Fails with a message when the roof is
        /// owned by another user or the editor cannot be enabled.
        /// </summary>
        public static string EnsureShapeEditorEnabled(Document doc, RoofBase roof)
        {
            SlabShapeEditor editor = roof.GetSlabShapeEditor();
            if (editor == null || editor.IsEnabled)
                return null;

            if (doc.IsWorkshared &&
                WorksharingUtils.GetCheckoutStatus(doc, roof.Id) == CheckoutStatus.OwnedByOtherUser)
                return "This roof is checked out by another user, so its shape editor cannot be enabled.";

            using Transaction tx = new Transaction(doc, "Enable Slab Shape Editor");
            tx.Start();
            editor.Enable();
            tx.Commit();
            return null;
        }
    }
}
