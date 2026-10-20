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
        private const string ModellingTabName  = "SloperPro-MDL_NT_03";
        private const string DocumentsTabName  = "SloperPro-DCM_NT_02";

        public Result OnStartup(UIControlledApplication application)
        {
            ToolGuard.Install();
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
            RoofToolsRibbon.Build(application, ModellingTabName, assemblyPath);
            DetailLInesRibbon.Build(application, ModellingTabName, assemblyPath);

            // ── SloperPro-Documents ──────────────────────────────────────────
            ViewToolsRibbon.Build(application, DocumentsTabName, assemblyPath);
            SetupRibbon.Build(application, DocumentsTabName, assemblyPath);
            SheetToolsRibbon.Build(application, DocumentsTabName, assemblyPath);
            ManageRibbon.Build(application, DocumentsTabName, assemblyPath);
        }
    }
}
