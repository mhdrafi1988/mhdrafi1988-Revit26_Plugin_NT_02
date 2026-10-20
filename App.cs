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
        private const string ModellingTabName  = "SloperPro-MDL_NT_00";
        private const string DocumentsTabName  = "SloperPro-DCM_NT_00";

        public Result OnStartup(UIControlledApplication application)
        {
            ToolGuard.Install();
            // Tracker ID V002: warn when elements with a Tracker_ID are deleted.
            TrackerId.V002.Infrastructure.Revit.TrackerDeletionWatcher.Register(application);
            // Once every add-in is loaded, warn if another copy of this one is loaded too: its old
            // styles can break tool windows.
            application.ControlledApplication.ApplicationInitialized += (_, _) => ToolGuard.WarnIfOtherCopiesLoaded();
            EnsureRibbonTabExists(application, ModellingTabName);
            EnsureRibbonTabExists(application, DocumentsTabName);
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

            // ── SloperPro-Modelling ──────────────────────────────────────────
            // Panels appear in the order they are created: workflow order, Utilities last.
            RoofToolsRibbon.Build(application, ModellingTabName, assemblyPath);
            DetailLInesRibbon.Build(application, ModellingTabName, assemblyPath);
            RoofToolsRibbon.BuildUtilities(application, ModellingTabName, assemblyPath);

            // ── SloperPro-Documents ──────────────────────────────────────────
            // Views, then sheets, then DWG/family setup, then model management.
            ViewToolsRibbon.Build(application, DocumentsTabName, assemblyPath);
            SheetToolsRibbon.Build(application, DocumentsTabName, assemblyPath);
            SetupRibbon.Build(application, DocumentsTabName, assemblyPath);
            ManageRibbon.Build(application, DocumentsTabName, assemblyPath);
        }
    }
}
