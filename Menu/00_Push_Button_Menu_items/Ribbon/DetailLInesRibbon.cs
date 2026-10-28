using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>
    /// Builds the Detail Lines panel: From Links, Closed Loop and Line Dims stacked in one column
    /// (a stack holds at most three items). Line Manager lives on the documents tab (<see cref="BuildLineManager"/>).
    /// Replaces the one-button Detail Line Create, Detail Line Process and Dimensions panels
    /// (plugin 2.4.0) so Revit has room to show every button name.
    /// </summary>
    public static class DetailLInesRibbon
    {
        /// <summary>Creates the Detail Lines panel on <paramref name="tabName"/>.</summary>
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Detail Lines");

            // Coexisting versions share one pulldown: highest version number first,
            // every older version below it in descending order. Each tool lists only
            // its latest two versions.
            var linesVA009 = new PushButtonData("Btn_ DeatailLInes VA009", "From Links VA009", assemblyPath, "Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Commands.OpenLinkedDetailLineGeneratorCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.Linematch32_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Detail Lines From Links", "VA009",
                    "VA008 on the shared ToolWindowShell (log in body, element-selection lists scroll with the body).")
            };
            var linesVA010 = new PushButtonData("Btn_ DeatailLInes VA010", "From Links VA010", assemblyPath, "Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Commands.OpenLinkedDetailLineGeneratorCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.Linematch32_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Detail Lines From Links", "VA010",
                    "VA009 + ToolViewModelBase (Options 1/2/3): base log/shell, 3-way partial split, TreeVisibilityFilter service.")
            };
            var linesPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DetailLinesFromLinks", "From Links", linesVA010);

            var closedLoopV002 = new PushButtonData("Btn_DetailLineClosedLoop_V002", "Closed Loop V002", assemblyPath, "Revit26_Plugin_Revised.DetailLineClosedLoop.V002.Commands.DetailLineClosedLoopCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.ClosedLoop_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Detail Line Closed Loop", "V002",
                    "UI Standard layout: two-column middle with a full-height Created Lines grid, footer log, Run → Close.")
            };
            var closedLoopV003 = new PushButtonData("Btn_DetailLineClosedLoop_V003", "Closed Loop V003", assemblyPath, "Revit26_Plugin_Revised.DetailLineClosedLoop.V003.Commands.DetailLineClosedLoopCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.ClosedLoop_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Detail Line Closed Loop", "V003",
                    "V002 on the shared ToolWindowShell (log in body, capped grid).")
            };
            var closedLoopPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DetailLineClosedLoop", "Closed Loop", closedLoopV003);

            var lineDimsPulldownData = DimensionsRibbon.CreateDetailLineDimPulldown(assemblyPath, out var lineDimsVersions);

            var items = RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData> { linesPulldownData, closedLoopPulldownData, lineDimsPulldownData });
            RibbonLayoutHelper.WirePulldownButton(items, "Pulldown_DetailLinesFromLinks", linesVA010, linesVA009);
            RibbonLayoutHelper.WirePulldownButton(items, "Pulldown_DetailLineClosedLoop", closedLoopV003, closedLoopV002);
            RibbonLayoutHelper.WirePulldownButton(items, DimensionsRibbon.DetailLineDimPulldownName, lineDimsVersions);

        }

        /// <summary>
        /// Creates the Detail Lines panel on the documents tab (<paramref name="tabName"/>) holding Line Manager
        /// as a large dropdown (V002, then V001). Moved from the modelling tab's Detail Lines panel in plugin 6.19.0.
        /// </summary>
        public static void BuildLineManager(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Detail Lines");

            // Line Manager lists V002 first (it supplies the dropdown's icon), then V001.
            var lineManagerV002 = new PushButtonData("Btn_DetailLineManager_V002", "Line Manager", assemblyPath, "Revit26_Plugin_Revised.DetailLineManager.V002.Commands.DetailLineManagerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.CreateDetailLines_16.png"),
                LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.CreateDetailLines_32.png"),
                ToolTip = ToolCatalog.DetailLineManagerV002.Tip(
                    "Everything in V001, plus a Rename styles tab: find/replace (regex), prefix, suffix and case for line styles, "
                    + "with a preview that flags duplicate and invalid names before anything changes.")
            };
            var lineManagerV001 = new PushButtonData("Btn_DetailLineManager_V001", "Line Manager (classic)", assemblyPath, "Revit26_Plugin_Revised.DetailLineManager.V001.Commands.DetailLineManagerCommand")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.CreateDetailLines_16.png"),
                LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.DetailLiner.CreateDetailLines_32.png"),
                ToolTip = ToolCatalog.DetailLineManager.Tip(
                    "Find detail lines by scope, style, view, weight and length. Delete duplicates, short lines and unused line styles, "
                    + "and delete or join overlapping lines; attached dimensions and tags are shown before you confirm. "
                    + "Assign styles or change line weights (style, per-view override).")
            };
            PulldownButtonData lineManagerPulldown = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DetailLineManager", "Line Manager", lineManagerV002);
            lineManagerPulldown.LargeImage = lineManagerV002.LargeImage;
            var lineManagerItem = panel.AddItem(lineManagerPulldown);
            RibbonLayoutHelper.WirePulldownButton(new List<RibbonItem> { lineManagerItem }, "Pulldown_DetailLineManager",
                lineManagerV002, lineManagerV001);
        }
    }
}
