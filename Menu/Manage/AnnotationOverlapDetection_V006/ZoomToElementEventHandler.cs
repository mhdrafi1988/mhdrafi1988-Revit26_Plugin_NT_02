using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006
{
    /// <summary>
    /// Step 8: zoom-on-click. The DataGrid hyperlink click happens on the WPF
    /// thread, which cannot touch the Revit API directly - so the click just
    /// sets ElementIdToZoom and calls Raise(). Revit then calls Execute() on
    /// its own thread.
    /// </summary>
    public class ZoomToElementEventHandler : IExternalEventHandler
    {
        public ElementId ElementIdToZoom { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (ElementIdToZoom == null)
                return;

            UIDocument uiDoc = app.ActiveUIDocument;
            Document doc = uiDoc.Document;
            Element elem = doc.GetElement(ElementIdToZoom);
            if (elem == null)
                return;

            BoundingBoxXYZ bbox = elem.get_BoundingBox(uiDoc.ActiveView);
            if (bbox == null)
                return;

            // 10% padding margin around the element (edge case: fit view with margin)
            XYZ size = bbox.Max - bbox.Min;
            XYZ padding = size * 0.10;
            var paddedBox = new BoundingBoxXYZ
            {
                Min = bbox.Min - padding,
                Max = bbox.Max + padding
            };

            UIView uiView = uiDoc.GetOpenUIViews()
                .FirstOrDefault(v => v.ViewId == uiDoc.ActiveView.Id);
            uiView?.ZoomAndCenterRectangle(paddedBox.Min, paddedBox.Max);

            // Optional 1-second highlight
            uiDoc.Selection.SetElementIds(new List<ElementId> { ElementIdToZoom });
        }

        public string GetName() => "Zoom To Annotation Element";
    }
}
