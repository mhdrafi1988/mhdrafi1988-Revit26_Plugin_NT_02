using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    public static class RoofToolsRibbon
    {
        // Every roof tool is a pulldown: highest version number first,
        // every older version below it in descending order. Each tool lists only
        // its latest two versions.
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.RoofTools.";

        private static PushButtonData Btn(string assemblyPath, string name, string text, string commandClass,
                                          string icon, string tool, string version, string detail = null)
        {
            return new PushButtonData(name, text, assemblyPath, commandClass)
            {
                Image = ImageUtils.Load(Icons + icon),
                ToolTip = RibbonLayoutHelper.VersionTip(tool, version, detail)
            };
        }

        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            // Utilities — three single-item panels merged into one 3-item stack
            // to save ribbon space: Combined Roof Tools, Ridge Lines, Type Manager.
            RibbonPanel utilitiesPanel = app.CreateRibbonPanel(tabName, "Utilities");

            // Slope — By Point (3 coexisting implementations) and By Drain (2)
            // each collected under one pulldown button. Each implementation's
            // UI-standard rebuild sits right after it in the dropdown.
            // Only versions whose source still exists are listed here.
            RibbonPanel slopePanel = app.CreateRibbonPanel(tabName, "Roof Slope");

            var byPointV029 = Btn(assemblyPath, "Btn_AutoSlopeByPoint_029", "By Point V029", "Revit26_Plugin_Revised.AutoSlopeByPoint.V029.Commands.AutoSlopeCommand",
                "by_point_16.png", "Auto Slope By Point", "V029", "V028 with the standard three-zone window layout.");
            var byPointV030 = Btn(assemblyPath, "Btn_AutoSlopeByPoint_030", "By Point V030", "Revit26_Plugin_Revised.AutoSlopeByPoint.V030.Commands.AutoSlopeCommand",
                "by_point_16.png", "Auto Slope By Point", "V030", "V029 on the shared ToolWindowShell (log in body).");
            var byPointV031 = Btn(assemblyPath, "Btn_AutoSlopeByPoint_031", "By Point V031", "Revit26_Plugin_Revised.AutoSlopeByPoint.V031.Commands.AutoSlopeCommand",
                "by_point_16.png", "Auto Slope By Point", "V031", "V030 with ToolGuard, single-instance window, pre-selected roof support.");
            var byPointV032 = Btn(assemblyPath, "Btn_AutoSlopeByPoint_032", "By Point V032", "Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Commands.AutoSlopeCommand",
                "by_point_16.png", "Auto Slope By Point", "V032", "V031 with ToolGuard.RunCommand, single-instance window, and shape editing guarded inside the TransactionGroup.");
            var byPointRidgeV002 = Btn(assemblyPath, "Btn_AutoSlopeByPointRidge_002", "By Point Ridge V002", "Revit26_Plugin_Revised.AutoSlopeByPointRidge.V002.Commands.AutoSlopeCommand",
                "AutoSlopeByPoint_Ridge_16.png", "Auto Slope By Point (Ridge)", "V002", "Ridge V001 with the standard three-zone window layout.");
            var byPointRidgeV003 = Btn(assemblyPath, "Btn_AutoSlopeByPointRidge_003", "By Point Ridge V003", "Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Commands.AutoSlopeCommand",
                "AutoSlopeByPoint_Ridge_16.png", "Auto Slope By Point (Ridge)", "V003", "Ridge V002 on the shared ToolWindowShell (log in body).");
            var byPointMultiCopies29 = Btn(assemblyPath, "Btn_AutoSlopeByPoint_MultiCopies29", "By Point Multi Copies 29", "Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies29.Commands.AutoSlopeCommand",
                "by_point_16.png", "Auto Slope By Point (Multi Copies)", "MultiCopies29", "MultiCopies28 with the standard three-zone window layout.");
            var byPointMultiCopies30 = Btn(assemblyPath, "Btn_AutoSlopeByPoint_MultiCopies30", "By Point Multi Copies 30", "Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30.Commands.AutoSlopeCommand",
                "by_point_16.png", "Auto Slope By Point (Multi Copies)", "MultiCopies30", "MultiCopies29 on the shared ToolWindowShell (log in body).");
            var byPointPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_AutoSlopeByPoint", "By Point", byPointV032);

            var byDrainV011 = Btn(assemblyPath, "Btn_AutoSlopeByDrain_V011", "By Drain V011", "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V011.Commands.AutoSlopeByDrain",
                "AutoSlopeByDrain_MultiRoof_16.png", "Auto Slope By Drain (Multi-Roof)", "V011", "V010 with the standard three-zone window layout.");
            var byDrainV012 = Btn(assemblyPath, "Btn_AutoSlopeByDrain_V012", "By Drain V012", "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V012.Commands.AutoSlopeByDrain",
                "AutoSlopeByDrain_MultiRoof_16.png", "Auto Slope By Drain (Multi-Roof)", "V012", "V011 on the shared ToolWindowShell (log in body, capped drains grid).");
            var byDrainV013 = Btn(assemblyPath, "Btn_AutoSlopeByDrain_V013", "By Drain V013", "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Commands.AutoSlopeByDrain",
                "AutoSlopeByDrain_MultiRoof_16.png", "Auto Slope By Drain (Multi-Roof)", "V013", "V012 with ToolGuard.RunCommand, single-instance window, and reset-on-run (no longer resets on pick).");
            var byDrainV007 = Btn(assemblyPath, "Btn_AutoSlopeByDrain_V007", "By Drain V007", "Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Commands.AutoSlopeByDrain",
                "by_drain_16.png", "Auto Slope By Drain", "V007", "Single-roof drain slope tool.");
            var byDrainPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_AutoSlopeByDrain", "By Drain", byDrainV013);

            var slopeItems = RibbonLayoutHelper.AddStackedButtons(slopePanel, new List<RibbonItemData> { byPointPulldownData, byDrainPulldownData });
            RibbonLayoutHelper.WirePulldownButton(slopeItems, "Pulldown_AutoSlopeByPoint", byPointV032, byPointV031, byPointV030, byPointV029, byPointRidgeV003, byPointRidgeV002, byPointMultiCopies30, byPointMultiCopies29);
            RibbonLayoutHelper.WirePulldownButton(slopeItems, "Pulldown_AutoSlopeByDrain", byDrainV013, byDrainV012, byDrainV011, byDrainV007);

            // Shape Points — one pulldown per tool (current version, then its rebuild)
            RibbonPanel shapePointsPanel = app.CreateRibbonPanel(tabName, "Shape Points");

            var innerLoopsV010 = Btn(assemblyPath, "Btn_InnerLoopDivider_V010", "Inner Loops V010", "Revit26_Plugin_Revised.InnerLoopDivider.V010.Commands.InnerLoopDividerCommand",
                "InnerLoopDivider_16.png", "Divide Inner Loops", "V010", "V009 with the standard three-zone window layout.");
            var innerLoopsV011 = Btn(assemblyPath, "Btn_InnerLoopDivider_V011", "Inner Loops V011", "Revit26_Plugin_Revised.InnerLoopDivider.V011.Commands.InnerLoopDividerCommand",
                "InnerLoopDivider_16.png", "Divide Inner Loops", "V011", "V010 on the shared ToolWindowShell (log in body, capped grid).");
            var innerLoopsV012 = Btn(assemblyPath, "Btn_InnerLoopDivider_V012", "Inner Loops V012", "Revit26_Plugin_Revised.InnerLoopDivider.V012.Commands.InnerLoopDividerCommand",
                "InnerLoopDivider_16.png", "Divide Inner Loops", "V012", "V011 improvements.");
            var outerCurveV005 = Btn(assemblyPath, "Btn_OuterCurveDivider_V005", "Outer Curve V005", "Revit26_Plugin_Revised.OuterCurveDivider.V005.Commands.CurveDividerCommand",
                "OuterCurveDivider_16.png", "Outer Curve Divider", "V005", "V004 with the standard three-zone window layout.");
            var outerCurveV006 = Btn(assemblyPath, "Btn_OuterCurveDivider_V006", "Outer Curve V006", "Revit26_Plugin_Revised.OuterCurveDivider.V006.Commands.CurveDividerCommand",
                "OuterCurveDivider_16.png", "Outer Curve Divider", "V006", "V005 on the shared ToolWindowShell (log in body, capped grids).");
            var lineIntersectV013 = Btn(assemblyPath, "Btn_RoofDetailLineIntersect_V013", "Line Intersect V013", "Revit26_Plugin_Revised.RoofDetailLineIntersect.V013.Commands.RoofDetailLineIntersectCommand",
                "DetailLineIntersect_16.png", "Roof Detail Line Intersect", "V013", "V012 with the standard three-zone window layout.");
            var lineIntersectV014 = Btn(assemblyPath, "Btn_RoofDetailLineIntersect_V014", "Line Intersect V014", "Revit26_Plugin_Revised.RoofDetailLineIntersect.V014.Commands.RoofDetailLineIntersectCommand",
                "DetailLineIntersect_16.png", "Roof Detail Line Intersect", "V014", "V013 on the shared ToolWindowShell (log in body).");
            var loopsPerpV006 = Btn(assemblyPath, "Btn_InnerLoopsAndPerpendicular_V006", "Loops + Perp. V006", "Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V006.Commands.InnerLoopsAndPerpendicularCommand",
                "InnerLoopsPerpendicular_16.png", "Inner Loops And Perpendicular", "V006", "V005 with the standard three-zone window layout.");
            var loopsPerpV007 = Btn(assemblyPath, "Btn_InnerLoopsAndPerpendicular_V007", "Loops + Perp. V007", "Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007.Commands.InnerLoopsAndPerpendicularCommand",
                "InnerLoopsPerpendicular_16.png", "Inner Loops And Perpendicular", "V007", "V006 on the shared ToolWindowShell (log in body, capped grids).");
            var vertexReducerV008 = Btn(assemblyPath, "Btn_VertexReducer_V008", "Vertex Reducer V008", "Revit26_Plugin_Revised.RoofEdgeVertexReducer.V008.Commands.RoofEdgeVertexReducerCommand",
                "VertexReducer_16.png", "Roof Edge Vertex Reducer", "V008", "V007 with the standard three-zone window layout.");
            var vertexReducerV009 = Btn(assemblyPath, "Btn_VertexReducer_V009", "Vertex Reducer V009", "Revit26_Plugin_Revised.RoofEdgeVertexReducer.V009.Commands.RoofEdgeVertexReducerCommand",
                "VertexReducer_16.png", "Roof Edge Vertex Reducer", "V009", "V008 on the shared ToolWindowShell (log in body, capped grid).");
            var multiPointsV002 = Btn(assemblyPath, "Btn_MultiplePoints_V002", "Multi Points V002", "Revit26_Plugin_Revised.MultiplePoints.V002.Commands.MultiplePointsCommand",
                "MultiplePoints_16.png", "Multiple Points (1/2–1/4)", "V002", "V001 with the standard three-zone window layout.");
            var multiPointsV003 = Btn(assemblyPath, "Btn_MultiplePoints_V003", "Multi Points V003", "Revit26_Plugin_Revised.MultiplePoints.V003.Commands.MultiplePointsCommand",
                "MultiplePoints_16.png", "Multiple Points (1/2–1/4)", "V003", "V002 on the shared ToolWindowShell (log in body, capped grid).");

            var shapePointsItems = RibbonLayoutHelper.AddStackedButtons(shapePointsPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_InnerLoopDivider", "Inner Loops", innerLoopsV012),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_OuterCurveDivider", "Outer Curve", outerCurveV006),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofDetailLineIntersect", "Line Intersect", lineIntersectV014),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_InnerLoopsAndPerpendicular", "Loops + Perp.", loopsPerpV007),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_VertexReducer", "Vertex Reducer", vertexReducerV009),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_MultiplePoints", "Multi Points", multiPointsV003),
            });
            RibbonLayoutHelper.WirePulldownButton(shapePointsItems, "Pulldown_InnerLoopDivider", innerLoopsV012, innerLoopsV011, innerLoopsV010);
            RibbonLayoutHelper.WirePulldownButton(shapePointsItems, "Pulldown_OuterCurveDivider", outerCurveV006, outerCurveV005);
            RibbonLayoutHelper.WirePulldownButton(shapePointsItems, "Pulldown_RoofDetailLineIntersect", lineIntersectV014, lineIntersectV013);
            RibbonLayoutHelper.WirePulldownButton(shapePointsItems, "Pulldown_InnerLoopsAndPerpendicular", loopsPerpV007, loopsPerpV006);
            RibbonLayoutHelper.WirePulldownButton(shapePointsItems, "Pulldown_VertexReducer", vertexReducerV009, vertexReducerV008);
            RibbonLayoutHelper.WirePulldownButton(shapePointsItems, "Pulldown_MultiplePoints", multiPointsV003, multiPointsV002);

            // Ridge Lines pulldown — merged into Utilities panel below.
            var ridgeLinesMultiShapeV069 = Btn(assemblyPath, "Btn_RoofRidgeLines_V69", "Ridge By Openings V069", "Revit26_Plugin_Revised.RoofRidgeLines.V069.Commands.RoofRidgeCommand",
                "RidgeLinesMultiShape_16.png", "Ridge By Openings", "V069", "V068 with the standard three-zone window layout.");
            var ridgeLinesMultiShapeV070 = Btn(assemblyPath, "Btn_RoofRidgeLines_V70", "Ridge By Openings V070", "Revit26_Plugin_Revised.RoofRidgeLines.V070.Commands.RoofRidgeCommand",
                "RidgeLinesMultiShape_16.png", "Ridge By Openings", "V070", "V069 on the shared ToolWindowShell (log in body, capped grid).");
            var ridgeLinesByPointsV058 = Btn(assemblyPath, "Btn_RoofRidgeLines_V58", "Ridge By Points V058", "Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V058.Commands.RoofRidgeCommand",
                "RidgeLinesByPoints_16.png", "Ridge By Points", "V058", "V057 with the standard three-zone window layout.");
            var ridgeLinesByPointsV059 = Btn(assemblyPath, "Btn_RoofRidgeLines_V59", "Ridge By Points V059", "Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V059.Commands.RoofRidgeCommand",
                "RidgeLinesByPoints_16.png", "Ridge By Points", "V059", "V058 on the shared ToolWindowShell (log in body, capped grid).");
            var ridgeLinesPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofRidgeLines", "Ridge Lines", ridgeLinesMultiShapeV070);

            // Slope Liner + Tag + Create — three single-tool panels merged into
            // one 3-item stack (Revit's stacked-item limit is exactly 3 per column).
            RibbonPanel roofToolsPanel = app.CreateRibbonPanel(tabName, "Roof Tools");

            var creaserV011 = Btn(assemblyPath, "Btn_CreaserAdvCommand_V011", "Creaser Adv V011", "Revit26_Plugin_Revised.CreaserAdv.V011.Commands.CreaserAdvCommand",
                "CreaserAdv_16.png", "Creaser Adv", "V011", "V010 with the standard three-zone window layout.");
            var creaserV012 = Btn(assemblyPath, "Btn_CreaserAdvCommand_V012", "Creaser Adv V012", "Revit26_Plugin_Revised.CreaserAdv.V012.Commands.CreaserAdvCommand",
                "CreaserAdv_16.png", "Creaser Adv", "V012", "V011 on the shared ToolWindowShell (log in body).");
            var roofTagV017 = Btn(assemblyPath, "Btn_RoofTagCommand_V017", "Roof Tag V017", "Revit26_Plugin_Revised.RoofTag.V017.RoofTagCommand",
                "RoofTag_16.png", "Roof Tag", "V017", "V016 with the standard three-zone window layout.");
            var roofTagV018 = Btn(assemblyPath, "Btn_RoofTagCommand_V018", "Roof Tag V018", "Revit26_Plugin_Revised.RoofTag.V018.RoofTagCommand",
                "RoofTag_16.png", "Roof Tag", "V018", "V017 on the shared ToolWindowShell (log in body).");

            // Roof From Detail Lines (V008/V009) removed in plugin 3.10.0: its source
            // is not in the repository, so the buttons could only raise an error.
            var roofToolsItems = RibbonLayoutHelper.AddStackedButtons(roofToolsPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_CreaserAdv", "Creaser Adv", creaserV012),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofTag", "Roof Tag", roofTagV018),
                // Moved here from the removed Floor Tools panel (plugin 2.4.0).
                FloorToolsRibbon.CreateFromRoomsPulldown(assemblyPath, out var fromRoomsVersions),
            });
            RibbonLayoutHelper.WirePulldownButton(roofToolsItems, "Pulldown_CreaserAdv", creaserV012, creaserV011);
            RibbonLayoutHelper.WirePulldownButton(roofToolsItems, "Pulldown_RoofTag", roofTagV018, roofTagV017);
            RibbonLayoutHelper.WirePulldownButton(roofToolsItems, FloorToolsRibbon.FromRoomsPulldownName, fromRoomsVersions);

            // Utilities panel: Combined Roof Tools + Ridge Lines + Type Manager (3-item stack)
            const string combinedDetail =
                "Inner Loop Divider, Inner Loops And Perpendicular, Outer Curve Divider, " +
                "Auto Slope By Drain, and Creaser Adv — combined in one window with one shared roof pick. " +
                "Opens on Auto Slope By Drain; use Run All to run every tool in order with one click.";
            var combinedV002 = Btn(assemblyPath, "Btn_CombinedRoofTools_V002", "Combined Roof Tools V002", "Revit26_Plugin_Revised.CombinedRoofTools.V002.Commands.CombinedRoofToolsCommand",
                "CombinedTools_16.png", "Combined Roof Tools", "V002", combinedDetail + " Standard three-zone window layout.");
            var combinedV003 = Btn(assemblyPath, "Btn_CombinedRoofTools_V003", "Combined Roof Tools V003", "Revit26_Plugin_Revised.CombinedRoofTools.V003.Commands.CombinedRoofToolsCommand",
                "CombinedTools_16.png", "Combined Roof Tools", "V003", "V002 on the shared ToolWindowShell (tab logs in body, capped grids).");

            const string typeManagerDetail =
                "Export roof type definitions to Excel (type mark, function, compound-structure layers) " +
                "and re-import them into the model. Modeless — stays open while you work.";
            var typeManagerV002 = Btn(assemblyPath, "Btn_RoofTypeManager_V002", "Roof Type Manager V002", "Revit26_Plugin_Revised.RoofTypeCreator.V002.Commands.RoofTypeManagerCommand",
                "CreateRoofromLInes_16.png", "Roof Type Manager", "V002", typeManagerDetail + " Standard three-zone window layout.");
            var typeManagerV003 = Btn(assemblyPath, "Btn_RoofTypeManager_V003", "Roof Type Manager V003", "Revit26_Plugin_Revised.RoofTypeCreator.V003.Commands.RoofTypeManagerCommand",
                "CreateRoofromLInes_16.png", "Roof Type Manager", "V003", "V002 on the shared ToolWindowShell (log in body, capped grids).");

            var utilitiesItems = RibbonLayoutHelper.AddStackedButtons(utilitiesPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_CombinedRoofTools", "Combined Tools", combinedV003),
                ridgeLinesPulldownData,
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofTypeManager", "Roof Types", typeManagerV003),
            });
            RibbonLayoutHelper.WirePulldownButton(utilitiesItems, "Pulldown_CombinedRoofTools", combinedV003, combinedV002);
            RibbonLayoutHelper.WirePulldownButton(utilitiesItems, "Pulldown_RoofRidgeLines", ridgeLinesMultiShapeV070, ridgeLinesMultiShapeV069, ridgeLinesByPointsV059, ridgeLinesByPointsV058);
            RibbonLayoutHelper.WirePulldownButton(utilitiesItems, "Pulldown_RoofTypeManager", typeManagerV003, typeManagerV002);

            // Roof Edit — Roof Name Builder (V002) and Island Roof Splitter (V002, V001 WIP).
            RibbonPanel roofEditPanel = app.CreateRibbonPanel(tabName, "Roof Edit");
            var nameBuilderV002 = Btn(assemblyPath, "Btn_RoofNameBuilder_V002", "Name Builder V002", "Revit26_Plugin_Revised.RoofNameBuilder.V002.Commands.RoofNameBuilderCommand",
                "CreateRoofromLInes_16.png", "Roof Name Builder", "V002", "Bulk rename roof types/instances with configurable INS/SLP/MAT codes; re-runs update existing codes.");
            var islandSplitterV001 = Btn(assemblyPath, "Btn_IslandRoofSplitter_V001", "Island Splitter V001", "Revit26_Plugin_Revised.IslandRoofSplitter.V001.Commands.IslandRoofSplitterCommand",
                "DetailLineIntersect_16.png", "Island Roof Splitter (WIP)", "V001", "Splits a roof into islands bounded by detail lines. Work in progress - test on a copy of your model.");
            var islandSplitterV002 = Btn(assemblyPath, "Btn_IslandRoofSplitter_V002", "Island Splitter V002", "Revit26_Plugin_Revised.IslandRoofSplitter.V002.Commands.IslandRoofSplitterCommand",
                "DetailLineIntersect_16.png", "Island Roof Splitter", "V002", "V001 plus an output choice (roof only / roof, floor if it fails / floor only) and a floor type picker. Test on a copy of your model.");
            var roofEditItems = RibbonLayoutHelper.AddStackedButtons(roofEditPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofNameBuilder", "Name Builder", nameBuilderV002),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_IslandRoofSplitter", "Island Splitter", islandSplitterV002),
            });
            RibbonLayoutHelper.WirePulldownButton(roofEditItems, "Pulldown_RoofNameBuilder", nameBuilderV002);
            RibbonLayoutHelper.WirePulldownButton(roofEditItems, "Pulldown_IslandRoofSplitter", islandSplitterV002, islandSplitterV001);

            // Compare
            RibbonPanel comparePanel = app.CreateRibbonPanel(tabName, "Compare");

            var comparisonV002 = Btn(assemblyPath, "Btn_RoofPointComparison_V002", "Comparison V002", "Revit26_Plugin_Revised.RoofPointComparison.V002.Commands.RoofComparisonCommand",
                "PointComparison_16.png", "Roof Point Comparison", "V002", "V001 with the standard three-zone window layout.");
            var comparisonV003 = Btn(assemblyPath, "Btn_RoofPointComparison_V003", "Comparison V003", "Revit26_Plugin_Revised.RoofPointComparison.V003.Commands.RoofComparisonCommand",
                "PointComparison_16.png", "Roof Point Comparison", "V003", "V002 on the shared ToolWindowShell (log in body).");

            var elevationSyncV004 = Btn(assemblyPath, "Btn_RoofPointElevationSync_V004", "Elevation Sync V004", "Revit26_Plugin_Revised.RoofPointElevationSync.V004.Command",
                "ElevationSync_16.png", "Roof Point Elevation Sync", "V004", "V003 with the standard three-zone window layout.");
            var elevationSyncV005 = Btn(assemblyPath, "Btn_RoofPointElevationSync_V005", "Elevation Sync V005", "Revit26_Plugin_Revised.RoofPointElevationSync.V005.Command",
                "ElevationSync_16.png", "Roof Point Elevation Sync", "V005", "V004 on the shared ToolWindowShell (log in body, capped grid).");
            var elevationSyncPulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofPointElevationSync", "Elevation Sync", elevationSyncV005);

            var compareItems = RibbonLayoutHelper.AddStackedButtons(comparePanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofPointComparison", "Comparison", comparisonV003),
                elevationSyncPulldownData,
            });
            RibbonLayoutHelper.WirePulldownButton(compareItems, "Pulldown_RoofPointComparison", comparisonV003, comparisonV002);
            RibbonLayoutHelper.WirePulldownButton(compareItems, "Pulldown_RoofPointElevationSync", elevationSyncV005, elevationSyncV004);

            // Insulation — Tapered EPS 2D Pattern (V006 highest, descending per CLAUDE.md rule)
            RibbonPanel insulationPanel = app.CreateRibbonPanel(tabName, "Insulation");

            var taperedEpsPattern2DV001 = Btn(assemblyPath,
                "Btn_TaperedEpsPattern2D_V001", "EPS Pattern 2D V001",
                "Revit26_Plugin_Revised.TaperedEpsPattern2D.V001.Commands.TaperedEpsPattern2DCommand",
                "TaperedEpsPattern2D_16.png", "Tapered EPS 2D Pattern", "V001",
                "Draws the tapered EPS board layout as detail lines on a plan view.");

            var taperedEpsPattern2DV002 = Btn(assemblyPath,
                "Btn_TaperedEpsPattern2D_V002", "EPS Pattern 2D V002",
                "Revit26_Plugin_Revised.TaperedEpsPattern2D.V002.Commands.TaperedEpsPattern2DCommand",
                "TaperedEpsPattern2D_V002_16.png", "Tapered EPS 2D Pattern", "V002",
                "V001 with grouping mode and outline edge info support.");

            var taperedEpsPattern2DV004 = Btn(assemblyPath,
                "Btn_TaperedEpsPattern2D_V004", "EPS Pattern 2D V004",
                "Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.Commands.TaperedEpsPattern2DCommand",
                "TaperedEpsPattern2D_V004_16.png", "Tapered EPS 2D Pattern", "V004",
                "V002 with row layout builder, row joints, collinear union and piece kind support.");

            var taperedEpsPattern2DV005 = Btn(assemblyPath,
                "Btn_TaperedEpsPattern2D_V005", "EPS Pattern 2D V005",
                "Revit26_Plugin_Revised.TaperedEpsPattern2D.V005.Commands.TaperedEpsPattern2DCommand",
                "TaperedEpsPattern2D_V004_16.png", "Tapered EPS 2D Pattern", "V005",
                "V004 with joint patterns (aligned/staggered), hatching with filled regions, and grouped colour-coded window.");

            var taperedEpsPattern2DV006 = Btn(assemblyPath,
                "Btn_TaperedEpsPattern2D_V006", "EPS Pattern 2D V006",
                "Revit26_Plugin_Revised.TaperedEpsPattern2D.V006.Commands.TaperedEpsPattern2DCommand",
                "TaperedEpsPattern2D_V004_16.png", "Tapered EPS 2D Pattern", "V006",
                "V005 with square board shape, row start E (from upper hip), best-fit joint offset, and board size comparison table.");

            var insulationItems = RibbonLayoutHelper.AddStackedButtons(insulationPanel,
                new List<RibbonItemData>
                {
                    RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_TaperedEpsPattern2D", "EPS 2D Pattern", taperedEpsPattern2DV006),
                });
            RibbonLayoutHelper.WirePulldownButton(insulationItems, "Pulldown_TaperedEpsPattern2D",
                taperedEpsPattern2DV006, taperedEpsPattern2DV005, taperedEpsPattern2DV004, taperedEpsPattern2DV002, taperedEpsPattern2DV001);

        }
    }
}
