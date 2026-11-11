using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// The Revit session the tool's window belongs to. The dispatcher refreshes
    /// <see cref="Application"/> on every job, so readers always see the current active document.
    /// </summary>
    public sealed class RevitContext
    {
        /// <summary>The Revit UI application.</summary>
        public UIApplication Application { get; set; }

        /// <summary>Creates the context for a session.</summary>
        public RevitContext(UIApplication application)
        {
            Application = application ?? throw new ArgumentNullException(nameof(application));
        }

        /// <summary>
        /// The active project document. Throws <see cref="InvalidOperationException"/> with a message for
        /// the user when no project is active.
        /// </summary>
        public Document RequireProject()
        {
            Document doc = Application.ActiveUIDocument?.Document;
            if (doc == null || !doc.IsValidObject)
                throw new InvalidOperationException("No project is open. Open a project and press Refresh.");
            if (doc.IsFamilyDocument)
                throw new InvalidOperationException("The active document is a family. Switch to a project and press Refresh.");
            return doc;
        }
    }
}
