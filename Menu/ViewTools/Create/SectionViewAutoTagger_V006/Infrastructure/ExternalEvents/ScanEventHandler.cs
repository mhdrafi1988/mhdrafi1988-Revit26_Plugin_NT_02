using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V006
{
    /// <summary>
    /// Handles all read-only scanning: sheets, section views on a sheet, and
    /// categories present in checked views. Separate from PlaceTagsEventHandler
    /// since this tool has two distinct Revit-touching action types
    /// (confirmed: two handlers, not one shared orchestrator).
    ///
    /// No transaction needed — all operations here are read-only queries.
    /// </summary>
    public class ScanEventHandler : IExternalEventHandler
    {
        public enum ScanMode
        {
            LoadSheets,
            LoadSectionViewsForSheet,
            ScanCategoriesForViews
        }

        public ScanMode Mode { get; set; }

        /// <summary>Input for LoadSectionViewsForSheet.</summary>
        public ElementId RequestedSheetId { get; set; }

        /// <summary>Input for ScanCategoriesForViews.</summary>
        public List<ElementId> RequestedViewIds { get; set; }

        /// <summary>Output: populated after Execute() runs, read by the ViewModel callback.</summary>
        public List<SheetOption> ResultSheets { get; private set; }
        public List<SectionViewOption> ResultSectionViews { get; private set; }
        public List<CategoryTagRow> ResultCategories { get; private set; }

        /// <summary>Raised on the UI thread (via ViewModel's captured Dispatcher) after Execute completes.</summary>
        public event Action<ScanMode> Completed;

        private readonly SheetScanService _sheetScan = new();
        private readonly CategoryScanService _categoryScan = new();

        /// <summary>
        /// True while a scan is in flight. The ViewModel checks this before
        /// calling Raise() again — without it, rapid checkbox/dropdown
        /// changes could overwrite Mode/RequestedSheetId/RequestedViewIds on
        /// this shared handler instance before the prior Execute() call
        /// runs, scanning with stale parameters.
        /// </summary>
        public bool IsPending { get; private set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            IsPending = true;
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                IsPending = false;
                Completed?.Invoke(Mode);
                return;
            }

            // Esc / Cancel with a Yes-No confirm during the (read-only) category scan.
            using ToolCancellation cancel = Mode == ScanMode.ScanCategoriesForViews
                ? ToolCancellation.BeginFor(ToolCatalog.SectionViewAutoTagger, app)
                : null;
            try
            {
                switch (Mode)
                {
                    case ScanMode.LoadSheets:
                        ResultSheets = _sheetScan.GetAllSheets(doc);
                        break;

                    case ScanMode.LoadSectionViewsForSheet:
                        ResultSectionViews = _sheetScan.GetSectionViewsOnSheet(doc, RequestedSheetId);
                        break;

                    case ScanMode.ScanCategoriesForViews:
                        ResultCategories = _categoryScan.ScanCategories(doc, RequestedViewIds ?? new List<ElementId>());
                        break;
                }
            }
            finally
            {
                IsPending = false;
                Completed?.Invoke(Mode);
            }
        }

        public string GetName() => "SectionViewAutoTagger Scan Handler";
    }
}
