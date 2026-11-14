using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Commands
{
    /// <summary>
    /// Annotation Overlap Report: lists overlapping annotations of every type on the active view or a chosen
    /// batch of views and sheets, with zoom-to-pair and Excel export. Report only: the command opens the
    /// modeless window and never changes the model.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AnnotationOverlapReportCommand : IExternalCommand
    {
        private static AnnotationOverlapReportWindow _window;

        /// <summary>Entry point called by Revit; delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData?.Application?.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.AnnotationOverlapReport.Name, "Open a model first.");
                return Result.Cancelled;
            }

            // One window at a time: bring the open one forward instead of opening a second.
            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            List<ViewEntry> views = ViewCatalog.List(doc);
            if (views.Count == 0)
            {
                TaskDialog.Show(ToolCatalog.AnnotationOverlapReport.Name,
                    "This model has no views the tool can check. Supported: " + ViewCatalog.SupportedList + ".");
                return Result.Cancelled;
            }

            // External events must be created here, inside a Revit API context.
            var scanHandler = new ScanEventHandler { Document = doc };
            var zoomHandler = new ZoomEventHandler { Document = doc };
            ExternalEvent scanEvent = ExternalEvent.Create(scanHandler);
            ExternalEvent zoomEvent = ExternalEvent.Create(zoomHandler);

            var viewModel = new AnnotationOverlapReportViewModel(views, scanEvent, scanHandler, zoomEvent, zoomHandler);
            var window = new AnnotationOverlapReportWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Closed += (_, _) =>
            {
                scanHandler.Completed = null;
                zoomHandler.Failed = null;
                scanEvent.Dispose();
                zoomEvent.Dispose();
                if (ReferenceEquals(_window, window))
                    _window = null;
            };

            _window = window;
            window.Show(); // modeless, so Revit stays usable while the list is open
            return Result.Succeeded;
        }
    }
}
