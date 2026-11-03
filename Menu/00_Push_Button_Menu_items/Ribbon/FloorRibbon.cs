using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>
    /// Builds the floor ribbon items. The Floors And Roof From Linked Rooms pulldown has sat in the
    /// Roof Tools panel (see <see cref="RoofToolsRibbon"/>) since plugin 2.4.0, when the old one-button
    /// Floor Tools panel was removed to keep the tab narrow. The Floor Slope panel (plugin 6.31.0)
    /// is built by <see cref="BuildSlopePanel"/>, right after the Roof Slope panel.
    /// </summary>
    public static class FloorToolsRibbon
    {
        /// <summary>Roof slope icons, reused by the floor slope buttons until floor icons exist.</summary>
        private const string RoofIcons = "Revit26_Plugin_Revised.Resources.Icons.RoofTools.";

        /// <summary>
        /// Creates the "Floor Slope" panel on <paramref name="tabName"/>, laid out like Roof Slope:
        /// a stack of By Drain (button), By Point (pulldown: Floor Slope By Point, its Ridge and its
        /// Multi Copies variants) and Points &amp; Slope (button). The By Point pulldown was added in
        /// plugin 6.32.0; the By Point button kept its internal name and moved into it.
        /// </summary>
        public static void BuildSlopePanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Slope");

            var byDrain = new PushButtonData("Btn_FloorSlopeByDrain_V001", "By Drain", assemblyPath,
                "Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands.FloorSlopeByDrainCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "by_drain_16.png"),
                LargeImage = ImageUtils.Load(RoofIcons + "by_drain_32.png"),
                ToolTip = ToolCatalog.FloorSlopeByDrain.Tip(
                    "Pick one or more floors and slope each one's shape points down to its drains: openings in the floor sketch " +
                    "and drain families (Plumbing Fixtures) on the floor. Asks before sloping a floor whose type has no Variable layer. One Undo.")
            };
            var byPoint = new PushButtonData("Btn_FloorSlopeByPoint_V001", "By Point", assemblyPath,
                "Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Commands.FloorSlopeByPointCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "by_point_16.png"),
                ToolTip = ToolCatalog.FloorSlopeByPoint.Tip(
                    "Pick a floor, then click the drain points on it; the floor's shape points are sloped down to them, " +
                    "with optional ridge lines between drain groups. Asks before sloping a floor whose type has no Variable layer.")
            };
            var byPointRidge = new PushButtonData("Btn_FloorSlopeByPointRidge_V001", "Ridge", assemblyPath,
                "Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Commands.FloorSlopeByPointRidgeCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "AutoSlopeByPoint_Ridge_16.png"),
                ToolTip = ToolCatalog.FloorSlopeByPointRidge.Tip(
                    "Pick a floor, then click the drain points on it. Groups the drains, lifts the ridge points between the groups " +
                    "so every point drains at the given slope, then slopes the floor. Asks before sloping a floor whose type has no Variable layer.")
            };
            var byPointMultiCopies = new PushButtonData("Btn_FloorSlopeByPointMultiCopies_V001", "Multi Copies", assemblyPath,
                "Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Commands.FloorSlopeByPointMultiCopiesCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "by_point_16.png"),
                ToolTip = ToolCatalog.FloorSlopeByPointMultiCopies.Tip(
                    "Pick a floor and its drain points, then tick several slopes: the floor is copied once per slope, each copy is " +
                    "sloped to the drains and put on its own workset (Floor_Slope_<n>%). Needs a workshared model.")
            };
            var byPointPulldown = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_FloorSlopeByPoint", "By Point", byPoint);

            var pointsAndSlope = new PushButtonData("Btn_FloorPointsAndSlope_V001", "Points & Slope", assemblyPath,
                "Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Commands.FloorPointsAndSlopeCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "CombinedTools_16.png"),
                LargeImage = ImageUtils.Load(RoofIcons + "CombinedTools_32.png"),
                ToolTip = ToolCatalog.FloorPointsAndSlope.Tip(
                    "Adds shape points around the floor's openings, perpendicular to the boundary, at mid/quarter points and in long gaps, " +
                    "builds the ridges between the chosen drain openings, then slopes the floor to them. One window, one Undo. Pick one floor or several.")
            };

            IList<RibbonItem> items = RibbonLayoutHelper.AddStackedButtons(panel,
                new List<RibbonItemData> { byDrain, byPointPulldown, pointsAndSlope });
            RibbonLayoutHelper.WirePulldownButton(items, "Pulldown_FloorSlopeByPoint", byPoint, byPointRidge, byPointMultiCopies);
        }

        /// <summary>
        /// Creates the "Floor Points" panel (plugin 6.33.0): the floor versions of the Shape Points tools,
        /// one plain button each (one version per tool), stacked 3 + 3 + 1. They reuse the roof icons.
        /// </summary>
        public static void BuildPointsPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Points");
            const string ns = "Revit26_Plugin_Revised.";

            PushButtonData Btn(string name, string text, string commandClass, string icon, ToolInfo tool, string detail) =>
                new PushButtonData(name, text, assemblyPath, ns + commandClass)
                {
                    Image = ImageUtils.Load(RoofIcons + icon + "_16.png"),
                    LargeImage = ImageUtils.Load(RoofIcons + icon + "_32.png"),
                    ToolTip = tool.Tip(detail)
                };

            var items = new List<RibbonItemData>
            {
                Btn("Btn_FloorInnerLoopDivider_V001", "Inner Loops", "FloorInnerLoopDivider.V001.Commands.InnerLoopDividerCommand",
                    "InnerLoopDivider", ToolCatalog.FloorInnerLoopDivider,
                    "Pick a floor: adds shape points around its openings (sketch openings and opening cuts), by count or spacing per opening."),
                Btn("Btn_FloorOuterCurveDivider_V001", "Outer Curve", "FloorOuterCurveDivider.V001.Commands.CurveDividerCommand",
                    "OuterCurveDivider", ToolCatalog.FloorOuterCurveDivider,
                    "Pick a floor: divides its curved boundary edges with shape points."),
                Btn("Btn_FloorDetailLineIntersect_V001", "Line Intersect", "FloorDetailLineIntersect.V001.Commands.FloorDetailLineIntersectCommand",
                    "DetailLineIntersect", ToolCatalog.FloorDetailLineIntersect,
                    "In a plan view, pick a floor and detail lines: adds shape points where the lines cross the floor's edges."),
                Btn("Btn_FloorInnerLoopsAndPerpendicular_V001", "Loops + Perp.", "FloorInnerLoopsAndPerpendicular.V001.Commands.InnerLoopsAndPerpendicularCommand",
                    "InnerLoopsPerpendicular", ToolCatalog.FloorInnerLoopsAndPerpendicular,
                    "Pick a floor: points around its openings plus perpendicular points to the boundary. Resets the floor's shape points to 0 when it opens."),
                Btn("Btn_FloorEdgeVertexReducer_V001", "Vertex Reducer", "FloorEdgeVertexReducer.V001.Commands.FloorEdgeVertexReducerCommand",
                    "VertexReducer", ToolCatalog.FloorEdgeVertexReducer,
                    "Removes extra shape points along the straight edges of a floor whose shape editing is on. Preview first."),
                Btn("Btn_FloorMultiplePoints_V001", "Multi Points", "FloorMultiplePoints.V001.Commands.MultiplePointsCommand",
                    "MultiplePoints", ToolCatalog.FloorMultiplePoints,
                    "Pick a floor: adds shape points at the half and quarter points of the edges you choose."),
                Btn("Btn_FloorShapeFromLink_V001", "Shape From Link", "FloorShapeFromLink.V001.Commands.FloorShapeFromLinkCommand",
                    "ElevationSync", ToolCatalog.FloorShapeFromLink,
                    "Select floors in a plan view, tick the linked files, and set each shape-editing point to the linked floor's top-surface " +
                    "(or nearest point) elevation minus a fixed number. Test on a copy of your model."),
            };

            RibbonLayoutHelper.AddStackedButtons(panel, items);
        }

        /// <summary>Internal name of the pulldown (unchanged since it lived in Floor Tools).</summary>
        public const string FromRoomsPulldownName = "Pulldown_FloorsAndRoofFromLinkedRooms";

        /// <summary>
        /// Returns the "Floor From Rooms" pulldown data to stack in a panel, and in
        /// <paramref name="versions"/> every version, highest first, for
        /// <see cref="RibbonLayoutHelper.WirePulldownButton"/>.
        /// </summary>
        public static PulldownButtonData CreateFromRoomsPulldown(string assemblyPath, out PushButtonData[] versions)
        {
            // Two approaches to the same job, collected under one pulldown
            // button per request — each approach lists its highest version
            // number first, every older version below it; V013 keeps the icon.
            var fromRoomsV012 = new PushButtonData("Btn_FloorsAndRoofFromLinkedRooms_V012", "From Rooms V012", assemblyPath, "Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V012.Command")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.FloorTools.FloorRoofFromRooms_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Floors And Roof From Linked Rooms", "V012",
                    "UI Standard layout: footer with progress, summaries, always-visible log and Create Floors / Create Roof → Close.")
            };
            var fromRoomsV013 = new PushButtonData("Btn_FloorsAndRoofFromLinkedRooms_V013", "From Rooms V013", assemblyPath, "Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V013.Command")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.FloorTools.FloorRoofFromRooms_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Floors And Roof From Linked Rooms", "V013",
                    "V012 on the shared ToolWindowShell (summaries and log in body, capped room grid).")
            };
            var fromRoomsPlanViewV005 = new PushButtonData("Btn_FloorsAndRoofFromLinkedRoomsViaPlanViewV005", "Rooms Plan View V005", assemblyPath, "Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRoomsViaPlanView.V005.Command")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.FloorTools.FloorRoofFromRoomsPlanView_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Floors And Roof From Linked Rooms (Via Plan View)", "V005",
                    "UI Standard layout: metrics card, full-height room list, footer log and Create Floors / Create Roof → Close.")
            };
            var fromRoomsPlanViewV006 = new PushButtonData("Btn_FloorsAndRoofFromLinkedRoomsViaPlanViewV006", "Rooms Plan View V006", assemblyPath, "Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRoomsViaPlanView.V006.Command")
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.FloorTools.FloorRoofFromRoomsPlanView_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Floors And Roof From Linked Rooms (Via Plan View)", "V006",
                    "V005 on the shared ToolWindowShell (summaries and log in body, capped room list).")
            };
            versions = new[] { fromRoomsV013, fromRoomsV012, fromRoomsPlanViewV006, fromRoomsPlanViewV005 };
            return RibbonLayoutHelper.CreatePulldownButtonData(FromRoomsPulldownName, "Floor From Rooms", fromRoomsV013);
        }
    }
}
