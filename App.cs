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
        private const string ModellingTabName  = "Rafi_Pro_MOds";
        private const string Modelling2TabName = "Rafi_Pro_MOds2";
        private const string DocumentsTabName  = "Rafi_Pro_Docs";
        private const string Documents2TabName = "Rafi_Pro_Docs2";

        public Result OnStartup(UIControlledApplication application)
        {
            ToolGuard.Install();
            // Tracker ID V002: warn when elements with a Tracker_ID are deleted.
            TrackerId.V002.Infrastructure.Revit.TrackerDeletionWatcher.Register(application);
            // Once every add-in is loaded, warn if another copy of this one is loaded too: its old
            // styles can break tool windows.
            application.ControlledApplication.ApplicationInitialized += (_, _) => ToolGuard.WarnIfOtherCopiesLoaded();
            EnsureRibbonTabExists(application, ModellingTabName);
            EnsureRibbonTabExists(application, Modelling2TabName);
            EnsureRibbonTabExists(application, DocumentsTabName);
            EnsureRibbonTabExists(application, Documents2TabName);
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

            // ── Modelling 1: roof and floor slope, points, lines, edit ──────
            RoofToolsRibbon.Build(application, ModellingTabName, assemblyPath);

            // ── Modelling 2: roof edit/compare, detail lines, utilities ─────
            RoofToolsRibbon.BuildEditComparePanels(application, Modelling2TabName, assemblyPath);
            DetailLInesRibbon.Build(application, Modelling2TabName, assemblyPath);
            RoofToolsRibbon.BuildUtilities(application, Modelling2TabName, assemblyPath);

            // ── Documents 1: views, then sheets ─────────────────────────────
            ViewToolsRibbon.Build(application, DocumentsTabName, assemblyPath);
            SheetToolsRibbon.Build(application, DocumentsTabName, assemblyPath);

            // ── Documents 2: tagging, DWG/family setup, model management ────
            TaggingRibbon.Build(application, Documents2TabName, assemblyPath);
            SetupRibbon.Build(application, Documents2TabName, assemblyPath);
            ManageRibbon.Build(application, Documents2TabName, assemblyPath);
            CoordinationRibbon.Build(application, Documents2TabName, assemblyPath);
        }
    }
}
