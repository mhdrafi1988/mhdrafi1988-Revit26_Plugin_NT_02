using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the DWG Tools and Family Tools panels of the SloperPro Documents tab.</summary>
    public static class SetupRibbon
    {
        /// <summary>Creates the DWG Tools panel, then the Family Tools panel, on <paramref name="tabName"/>.</summary>
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            // DWG Tools first, then Family Tools.
            BuildDwgToolsPanel(app, tabName, assemblyPath);
            BuildFamilyToolsPanel(app, tabName, assemblyPath);
        }

        private static void BuildFamilyToolsPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            // Family-editor tools and project DWG tools are split into their own
            // panels — running a Family tool in a Project (or vice versa)
            // fails the context check, so the panel titles make that clear.
            RibbonPanel familyPanel = app.CreateRibbonPanel(tabName, "Family Tools");

            // Every tool is a pulldown: highest version number first, every
            // older version below it in descending order.
            // Batch Link DWG's original build has no version number in its code, so the tip
            // says "Working build"; its folder is BatchDwgFamilyLinker_V001 (renamed from
            // BatchDwgFamilyLinker_Working in plugin 5.8.0). V002 is the newer, higher version.
            var batchLinkWorking = new PushButtonData("BatchLinkDwgCommand", "Batch Link DWG (Working)", assemblyPath, "BatchDwgFamilyLinker.Command.BatchLinkDwgCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.Linker_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Batch Link DWG Family", "Working build")
            };
            var batchLinkV002 = new PushButtonData("Btn_BatchLinkDwg_V002", "Batch Link DWG V002", assemblyPath, "BatchDwgFamilyLinker.V002.Command.BatchLinkDwgCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.Linker_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Batch Link DWG Family", "V002", "UI Standard layout: navy theme, metrics card, footer progress + live log with Copy All / Copy Selected, Start Batch → Close.")
            };
            var batchLinkPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_BatchLinkDwg", "Batch Link DWG", batchLinkV002);

            var dwgToLinesV006 = new PushButtonData("Btn_DwgToLines_V006", "DWG To Lines V006", assemblyPath, "Revit26_Plugin_Revised.DwgToLines.V006.Commands.DwgToLinesCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToLines_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("DWG To Lines", "V006", "UI Standard layout: metrics card, always-visible footer log, Convert DWG → Close.")
            };
            var dwgToLinesV007 = new PushButtonData("Btn_DwgToLines_V007", "DWG To Lines V007", assemblyPath, "Revit26_Plugin_Revised.DwgToLines.V007.Commands.DwgToLinesCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToLines_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("DWG To Lines", "V007", "V006 on the shared ToolWindowShell (log in body).")
            };
            var dwgToLinesPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DwgToLines", "DWG To Lines", dwgToLinesV007);

            var familyItems = RibbonLayoutHelper.AddStackedButtons(familyPanel, new List<RibbonItemData>
            {
                batchLinkPulldownData,
                dwgToLinesPulldownData,
            });
            RibbonLayoutHelper.WirePulldownButton(familyItems, "Pulldown_BatchLinkDwg", batchLinkV002, batchLinkWorking);
            RibbonLayoutHelper.WirePulldownButton(familyItems, "Pulldown_DwgToLines", dwgToLinesV007, dwgToLinesV006);
        }

        private static void BuildDwgToolsPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            // Every button on this panel is a DWG tool, hence the panel name.
            // Project context only — the two family-editor DWG tools stay on
            // the Family Tools panel.
            RibbonPanel projectPanel = app.CreateRibbonPanel(tabName, "DWG Tools");

            // Latest two versions of the tool, collected under one pulldown
            // button — each version is a push button inside the dropdown.
            var dwgLinesV013 = new PushButtonData("Btn_DwgToDetailLines_V013", "DWG To DL(P) V013", assemblyPath, "Revit26_Plugin_Revised.DwgToDetailLines.V013.Commands.DwgToDetailLinesCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_V011_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("DWG To Detail Lines", "V013", "V012 on the shared ToolWindowShell (log in body, capped grid).")
            };
            var dwgLinesV014 = new PushButtonData("Btn_DwgToDetailLines_V014", "DWG To DL(P) V014", assemblyPath, "Revit26_Plugin_Revised.DwgToDetailLines.V014.Commands.DwgToDetailLinesCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_V011_16.png"),
                ToolTip = ToolCatalog.DwgToDlp.Tip("Convert a DWG to detail lines and hatches. Map one global line style and hatch to every layer, or pick a style per layer.")
            };
            var dwgLinesPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DwgToDetailLines", "DWG To DL(P)", dwgLinesV014);

            // Newest build first.
            var exportDwgV004 = new PushButtonData("Btn_ExportDwgToFolder_V004", "Export DWG V004", assemblyPath, "Revit26_Plugin_Revised.ExportDwgToFolder.V004.Commands.ExportDwgToFolderCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_V011_16.png"),
                ToolTip = ToolCatalog.ExportDwgToFolder.Tip("Export embedded CAD links and imports to a folder, with layer-property and post-export review options, plus an export manifest.")
            };
            var exportDwgV003 = new PushButtonData("Btn_ExportDwgToFolder_V003", "Export DWG V003", assemblyPath, "Revit26_Plugin_Revised.ExportDwgToFolder.V003.Commands.ExportDwgToFolderCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_V011_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Export DWG to Folder", "V003", "Export all embedded CAD links and imports to a folder, organised by source type and view category.")
            };
            // V001 removed in plugin 4.2.1: its source is not in the repository.
            var exportDwgPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_ExportDwgToFolder", "Export DWG", exportDwgV004);

            var dwgManagerV002 = new PushButtonData("Btn_DwgManager_V002", "DWG Manager V002", assemblyPath, "Revit26_Plugin_Revised.DwgManager.V002.Commands.DwgManagerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_16.png"),
                ToolTip = ToolCatalog.DwgManager.Tip("List every CAD link and import by link type and view, open up to 5 of their views, select instances by ID, and delete them (optionally purging types no longer placed anywhere).")
            };
            var dwgManagerV003 = new PushButtonData("Btn_DwgManager_V003", "DWG Manager V003", assemblyPath, "Revit26_Plugin_Revised.DwgManager.V003.Commands.DwgManagerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_16.png"),
                ToolTip = ToolCatalog.DwgManager.Tip("List every CAD link and import by link type and view, open up to 5 of their views, select instances by ID, and delete them (optionally purging types no longer placed anywhere).")
            };
            var dwgManagerPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DwgManager", "DWG Manager", dwgManagerV003);

            var dwgStatisticsV005 = new PushButtonData("Btn_DwgStatistics_V005", "DWG Statistics V005", assemblyPath, "Revit26_Plugin_Revised.DwgStatistics.V005.Commands.DwgStatisticsCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SetupTools.DwgToDetailLines_16.png"),
                ToolTip = ToolCatalog.DwgStatistics.Tip("Report DWG usage statistics across the project.")
            };
            var dwgStatisticsPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DwgStatistics", "DWG Statistics", dwgStatisticsV005);

            var projectItems = RibbonLayoutHelper.AddStackedButtons(projectPanel, new List<RibbonItemData>
            {
                dwgLinesPulldownData,
                exportDwgPulldownData,
                dwgManagerPulldownData,
                dwgStatisticsPulldownData,
            });
            RibbonLayoutHelper.WirePulldownButton(projectItems, "Pulldown_DwgToDetailLines", dwgLinesV014, dwgLinesV013);
            RibbonLayoutHelper.WirePulldownButton(projectItems, "Pulldown_ExportDwgToFolder", exportDwgV004, exportDwgV003);
            RibbonLayoutHelper.WirePulldownButton(projectItems, "Pulldown_DwgManager", dwgManagerV003, dwgManagerV002);
            RibbonLayoutHelper.WirePulldownButton(projectItems, "Pulldown_DwgStatistics", dwgStatisticsV005);
        }
    }
}
