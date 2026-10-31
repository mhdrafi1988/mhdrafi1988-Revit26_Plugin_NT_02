using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    public static class SheetToolsRibbon
    {
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel createPanel = app.CreateRibbonPanel(tabName, "Sheet Create");

            // Coexisting versions of each tool share one pulldown button; the
            // highest version number stays first, every older version below it.
            // Each tool lists only its latest two versions.
            var scopeBoxV005 = new PushButtonData("Btn_PlanFromScopeBox.V005", "Scope Box V005", assemblyPath, "Revit26_Plugin_Revised.PlanFromScopeBox.V005.Commands.PlanFromScopeBoxCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.PlanFromScopeBox_V004_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Plan From Scope Box", "V005", "UI Standard layout: fixed header and metrics, footer log with Copy All / Copy Selected, Create → Close.")
            };
            var scopeBoxV006 = new PushButtonData("Btn_PlanFromScopeBox.V006", "Scope Box V006", assemblyPath, "Revit26_Plugin_Revised.PlanFromScopeBox.V006.Commands.PlanFromScopeBoxCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.PlanFromScopeBox_V004_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Plan From Scope Box", "V006", "V005 on the shared ToolWindowShell (log in body, capped grid).")
            };
            var scopeBoxPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_PlanFromScopeBox", "Scope Box", scopeBoxV006);

            var sheetPlacerV223 = new PushButtonData("Btn_SmartViewToSheetPlacer.V223", "Sheet Placer V223", assemblyPath, "Revit26_Plugin_Revised.SmartViewToSheetPlacer.V223.SmartViewToSheetPlacerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SmartViewToSheetPlacer_V222_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Smart View To Sheet Placer", "V223", "UI Standard layout: fixed footer with the Activity Log (Copy All / Copy Selected), Export Logs and Close.")
            };
            var sheetPlacerV224 = new PushButtonData("Btn_SmartViewToSheetPlacer.V224", "Sheet Placer V224", assemblyPath, "Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.SmartViewToSheetPlacerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SmartViewToSheetPlacer_V222_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Smart View To Sheet Placer", "V224", "V223 on the shared ToolWindowShell (progress and log in body, capped grids).")
            };
            var sheetPlacerPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_SmartViewToSheetPlacer", "Sheet Placer", sheetPlacerV224);

            // Automated Section Placer: standalone tool (not a SmartViewToSheetPlacer
            // version) — detects section views visible on the active Plan View and
            // runs them through the same packing/placement pipeline.
            // TODO: swap for a dedicated icon — temporarily reusing the Sheet Placer icon.
            var autoSectionPlacerV002 = new PushButtonData("Btn_AutomatedSectionPlacer_V002", "Auto Section Placer V002", assemblyPath, "Revit26_Plugin_Revised.AutomatedSectionPlacer.V002.AutomatedSectionPlacerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SmartViewToSheetPlacer_V222_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Automated Section Placer", "V002", "UI Standard layout: fixed footer with the Activity Log (Copy All / Copy Selected), Export Logs and Close.")
            };
            var autoSectionPlacerV003 = new PushButtonData("Btn_AutomatedSectionPlacer_V003", "Auto Section Placer V003", assemblyPath, "Revit26_Plugin_Revised.AutomatedSectionPlacer.V003.AutomatedSectionPlacerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SmartViewToSheetPlacer_V222_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Automated Section Placer", "V003", "V002 on the shared ToolWindowShell (progress and log in body, capped grids).")
            };
            var autoSectionPlacerPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_AutomatedSectionPlacer", "Section Placer", autoSectionPlacerV003);

            // Sections To Sheet: separate tool — takes the sections shown in (or selected in) the
            // active view off their old sheets and lays them out on a new sheet.
            var sectionsToSheetV001 = new PushButtonData("Btn_SectionsToSheet_V001", "Sections To Sheet", assemblyPath, "Revit26_Plugin_Revised.SectionsToSheet.V001.Commands.SectionsToSheetCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SectionsToSheet_16.png"),
                LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SectionsToSheet_32.png"),
                ToolTip = ToolCatalog.SectionsToSheet.Tip(
                    "Lists the section markers shown in the active view (or the selected ones, or picked ones), takes them off " +
                    "the sheet they are on, and lays them out on a new sheet: columns that line up, or justified rows, " +
                    "in plan order, bottom-aligned so titles line up. Live preview before anything changes; one Undo step.")
            };

            // Sheet Text Replace: finds and replaces text in the text notes on a sheet, in the views
            // the user ticks (and in notes placed on the sheet itself).
            var sheetTextReplaceV001 = new PushButtonData("Btn_SheetTextReplace.V001", "Text Replace", assemblyPath, "Revit26_Plugin_Revised.SheetTextReplace.V001.Commands.SheetTextReplaceCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SheetTextReplace_16.png"),
                LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SheetTextReplace_32.png"),
                ToolTip = ToolCatalog.SheetTextReplace.Tip("Find and replace text notes in selected views on a sheet.")
            };

            // Rearrange and Arrange Views follow in the same panel (the one-column Sheet Place
            // panel was merged in, plugin 2.4.0, so Revit has room to show every button name).
            var rearrangeV027 = new PushButtonData("Btn_SheetAutoRearrange.V027", "Rearrange V027", assemblyPath, "Revit26_Plugin_Revised.SheetAutoRearrange.V027.Commands.SheetAutoRearrangeCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SheetAutoRearrange_V026_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Sheet Auto Rearrange", "V027", "UI Standard layout: metrics on top, always-visible log in the footer (Copy All / Copy Selected), Run → Close.")
            };
            var rearrangeV028 = new PushButtonData("Btn_SheetAutoRearrange.V028", "Rearrange V028", assemblyPath, "Revit26_Plugin_Revised.SheetAutoRearrange.V028.Commands.SheetAutoRearrangeCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SheetAutoRearrange_V026_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Sheet Auto Rearrange", "V028", "V027 on the shared ToolWindowShell (log in body, capped grid).")
            };
            var rearrangePulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_SheetAutoRearrange", "Rearrange", rearrangeV028);

            // Sheet View Arrange: separate tool (not a Rearrange version) — keeps the views'
            // sizes, orders them by detail number and lays them out as a reading table.
            // TODO: swap for a dedicated icon — temporarily reusing the Rearrange icon.
            var viewArrangeV001 = new PushButtonData("Btn_SheetViewArrange.V001", "Arrange Views", assemblyPath, "Revit26_Plugin_Revised.SheetViewArrange.V001.Commands.SheetViewArrangeCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SheetAutoRearrange_16.png"),
                LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.SheetTools.SheetAutoRearrange_32.png"),
                ToolTip = ToolCatalog.SheetViewArrange.Tip(
                    "Orders the views already on the active sheet by detail number and lays them out like a reading table: " +
                    "left to right, rows top to bottom, evenly spread and bottom-aligned so titles line up.")
            };

            // Slide-out (plugin 6.13.0): Rearrange and Arrange Views come after creating and placing views.
            var sheetCreateItems = RibbonLayoutHelper.AddStackedButtonsWithSlideOut(createPanel,
                new List<RibbonItemData> { scopeBoxPulldownData, sheetPlacerPulldownData, autoSectionPlacerPulldownData, sectionsToSheetV001 },
                new List<RibbonItemData> { rearrangePulldownData, viewArrangeV001 });
            RibbonLayoutHelper.WirePulldownButton(sheetCreateItems, "Pulldown_PlanFromScopeBox", scopeBoxV006, scopeBoxV005);
            RibbonLayoutHelper.WirePulldownButton(sheetCreateItems, "Pulldown_SmartViewToSheetPlacer", sheetPlacerV224, sheetPlacerV223);
            RibbonLayoutHelper.WirePulldownButton(sheetCreateItems, "Pulldown_AutomatedSectionPlacer", autoSectionPlacerV003, autoSectionPlacerV002);
            RibbonLayoutHelper.WirePulldownButton(sheetCreateItems, "Pulldown_SheetAutoRearrange", rearrangeV028, rearrangeV027);

            // Sheet Edit panel (plugin 6.22.0): tools that change what is already on a sheet.
            // A lone item is added as a full-size button with its 32 px icon.
            RibbonPanel editPanel = app.CreateRibbonPanel(tabName, "Sheet Edit");
            RibbonLayoutHelper.AddStackedButtons(editPanel, new List<RibbonItemData> { sheetTextReplaceV001 });
        }
    }
}
