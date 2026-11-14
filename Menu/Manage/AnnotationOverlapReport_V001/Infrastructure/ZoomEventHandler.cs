using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure
{
    /// <summary>
    /// Zoom to pair: opens the view the pair was found in, zooms to both annotations and selects them.
    /// Runs on the Revit thread through an external event. Changes nothing in the model.
    /// </summary>
    public sealed class ZoomEventHandler : IExternalEventHandler
    {
        /// <summary>Document the window was opened on.</summary>
        public Document Document { get; set; }

        /// <summary>View to open.</summary>
        public long ViewId { get; set; }

        /// <summary>First annotation of the pair.</summary>
        public long IdA { get; set; }

        /// <summary>Second annotation of the pair.</summary>
        public long IdB { get; set; }

        /// <summary>Called with a message when the pair could not be shown.</summary>
        public Action<string> Failed { get; set; }

        /// <inheritdoc />
        public void Execute(UIApplication app) => ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        /// <inheritdoc />
        public string GetName() => "Annotation Overlap Report: zoom to pair";

        private void ExecuteCore(UIApplication app)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            Document doc = Document;
            if (doc == null || !doc.IsValidObject || uiDoc == null || !uiDoc.Document.Equals(doc))
            {
                Failed?.Invoke("The model this window was opened on is closed or not active.");
                return;
            }

            if (!(doc.GetElement(new ElementId(ViewId)) is View view) || !ViewCatalog.IsSupported(view))
            {
                Failed?.Invoke("That view no longer exists. Run the check again.");
                return;
            }

            var ids = new List<ElementId>();
            foreach (long id in new[] { IdA, IdB })
            {
                Element element = doc.GetElement(new ElementId(id));
                if (element != null && element.IsValidObject)
                    ids.Add(element.Id);
            }
            if (ids.Count == 0)
            {
                Failed?.Invoke("Both annotations were deleted. Run the check again.");
                return;
            }

            try
            {
                if (uiDoc.ActiveView == null || uiDoc.ActiveView.Id != view.Id)
                    uiDoc.ActiveView = view;
                uiDoc.ShowElements(ids);
                uiDoc.Selection.SetElementIds(ids);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
            {
                Failed?.Invoke("Could not open the view: " + ex.Message);
            }
        }
    }
}
