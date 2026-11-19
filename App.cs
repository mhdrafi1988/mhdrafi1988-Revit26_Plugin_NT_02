using System.Reflection;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Menu.Ribbon;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised
{
    /// <summary>
    /// Entry point for the Revit add-in.
    /// </summary>
    public partial class App : IExternalApplication
    {
        private const string RoofTabName   = "Rafi_Pro_Roof";
        private const string FloorsTabName = "Rafi_Pro_Floors";
        private const string ModelTabName  = "Rafi_Pro_Model";
        private const string DocsTabName   = "Rafi_Pro_Docs";

        public Result OnStartup(UIControlledApplication application)
        {
            ToolGuard.Install();
            // Tracker ID V002: warn when elements with a Tracker_ID are deleted.
            TrackerId.V002.Infrastructure.Revit.TrackerDeletionWatcher.Register(application);
            // Once every add-in is loaded, warn if another copy of this one is loaded too: its old
            // styles can break tool windows.
            application.ControlledApplication.ApplicationInitialized += (_, _) => ToolGuard.WarnIfOtherCopiesLoaded();
            EnsureRibbonTabExists(application, RoofTabName);
            EnsureRibbonTabExists(application, FloorsTabName);
            EnsureRibbonTabExists(application, ModelTabName);
            EnsureRibbonTabExists(application, DocsTabName);
            InitializeRibbonPanels(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void EnsureRibbonTabExists(UIControlledApplication application, string tabName)
        {
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists – ignore
            }
        }

        private void InitializeRibbonPanels(UIControlledApplication application)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // Panels appear in the order they are created, so each tab is built in workflow order.

            // ── Roof: all roof panels on one tab ───────────────────────────
            RoofToolsRibbon.Build(application, RoofTabName, assemblyPath);
            RoofToolsRibbon.BuildEditComparePanels(application, RoofTabName, assemblyPath);
            RoofToolsRibbon.BuildUtilities(application, RoofTabName, assemblyPath);

            // ── Floors: floor tools + waterproofing ────────────────────────
            FloorToolsRibbon.Build(application, FloorsTabName, assemblyPath);
            SheetToolsRibbon.BuildWaterproofingPanel(application, FloorsTabName, assemblyPath);

            // ── Model: coordination, manage, worksets, DWG/family setup ────
            CoordinationRibbon.Build(application, ModelTabName, assemblyPath);
            ManageRibbon.Build(application, ModelTabName, assemblyPath);
            SetupRibbon.Build(application, ModelTabName, assemblyPath);

            // ── Docs: views, sheets, tagging, detail lines ─────────────────
            ViewToolsRibbon.Build(application, DocsTabName, assemblyPath);
            SheetToolsRibbon.Build(application, DocsTabName, assemblyPath);
            TaggingRibbon.Build(application, DocsTabName, assemblyPath);
            DetailLInesRibbon.Build(application, DocsTabName, assemblyPath);
        }
    }
}
