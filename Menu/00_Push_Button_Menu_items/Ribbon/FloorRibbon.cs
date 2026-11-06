using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Windows.Media;

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
        /// <summary>Green used for the Floor panel title bars, matching the Floor ribbon icons.</summary>
        private static readonly Color FloorGreen = Color.FromRgb(33, 130, 49);

        /// <summary>
        /// Colours the title bar of a Floor panel green so the Floor tools stand apart from the Roof tools.
        /// Cosmetic only: a failure here is ignored and never stops the ribbon from loading.
        /// </summary>
        /// <param name="panel">The Floor ribbon panel just created.</param>
        private static void ApplyFloorPanelColor(RibbonPanel panel)
        {
            try
            {
                var brush = new SolidColorBrush(FloorGreen);
                brush.Freeze();
                panel.CustomPanelTitleBarBackground = brush;
            }
            catch
            {
                // Cosmetic only.
            }
        }

        /// <summary>Roof slope icons, reused by the floor slope buttons until floor icons exist.</summary>
        private const string RoofIcons = "Revit26_Plugin_Revised.Resources.Icons.FloorTools.";

        /// <summary>
        /// Creates the "Floor Slope" panel on <paramref name="tabName"/>, laid out like Roof Slope:
        /// a stack of By Drain (button), By Point (pulldown: Floor Slope By Point, its Ridge and its
        /// Multi Copies variants) and Points &amp; Slope (button). The By Point pulldown was added in
        /// plugin 6.33.0; the By Point button kept its internal name and moved into it.
        /// </summary>
        public static void BuildSlopePanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Slope");
            ApplyFloorPanelColor(panel);

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
        /// Creates the "Floor Points" panel (plugin 6.34.0): the floor versions of the Shape Points tools,
        /// one plain button each (one version per tool), stacked 3 + 3 + 1. They reuse the roof icons.
        /// </summary>
        public static void BuildPointsPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Points");
            ApplyFloorPanelColor(panel);
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

        /// <summary>
        /// Creates the "Floor Lines" panel (plugin 6.35.0): Floor Crease Lines By Openings, Floor Crease Lines
        /// By Points and Floor Creaser as one stack of three plain buttons (one version each). They reuse the
        /// roof Ridge Lines and Creaser Adv icons.
        /// </summary>
        public static void BuildLinesPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Lines");
            ApplyFloorPanelColor(panel);

            var byOpenings = new PushButtonData("Btn_FloorCreaseLinesByOpenings_V001", "Crease By Openings", assemblyPath,
                "Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Commands.FloorRidgeCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "RidgeLinesMultiShape_16.png"),
                ToolTip = ToolCatalog.FloorCreaseLinesByOpenings.Tip(
                    "Pick a floor and choose openings as drains: draws the crease (ridge) lines midway between them as " +
                    "shape points + split lines and/or detail lines. Run it before sloping.")
            };
            var byPoints = new PushButtonData("Btn_FloorCreaseLinesByPoints_V001", "Crease By Points", assemblyPath,
                "Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Commands.FloorRidgeCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "RidgeLinesByPoints_16.png"),
                ToolTip = ToolCatalog.FloorCreaseLinesByPoints.Tip(
                    "Pick a floor and click drain points on it: draws the crease (ridge) lines midway between them as " +
                    "shape points + split lines and/or detail lines.")
            };
            var creaser = new PushButtonData("Btn_FloorCreaser_V001", "Floor Creaser", assemblyPath,
                "Revit26_Plugin_Revised.FloorCreaser.V001.Commands.CreaserAdvCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "CreaserAdv_16.png"),
                ToolTip = ToolCatalog.FloorCreaser.Tip(
                    "Pick a sloped floor: places detail items along its creases (the edges between its top-face facets) in the active view. " +
                    "Reads the floor only; it does not change it.")
            };

            RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData> { byOpenings, byPoints, creaser });
        }

        /// <summary>
        /// Creates the "Floor Edit" panel (plugin 6.36.0): Floor Types, Name Builder, Floor Tag, Comparison,
        /// Elevation Sync and (6.37.0) Floor EPS, one plain button each (one version each), stacked 3 + 3.
        /// They reuse the roof icons.
        /// </summary>
        public static void BuildEditPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Edit");
            ApplyFloorPanelColor(panel);

            PushButtonData Btn(string name, string text, string commandClass, string icon, ToolInfo tool, string detail) =>
                new PushButtonData(name, text, assemblyPath, "Revit26_Plugin_Revised." + commandClass)
                {
                    Image = ImageUtils.Load(RoofIcons + icon + "_16.png"),
                    ToolTip = tool.Tip(detail)
                };

            var items = new List<RibbonItemData>
            {
                Btn("Btn_FloorTypeManager_V001", "Floor Types", "FloorTypeManager.V001.Commands.FloorTypeManagerCommand",
                    "CreateRoofromLInes", ToolCatalog.FloorTypeManager,
                    "Lists the project's floor types with their layers; exports them to Excel and creates new floor types from an Excel template."),
                Btn("Btn_FloorNameBuilder_V001", "Name Builder", "FloorNameBuilder.V001.Commands.FloorNameBuilderCommand",
                    "CreateRoofromLInes", ToolCatalog.FloorNameBuilder,
                    "Bulk-renames floor types (or writes a floor instance parameter) from configurable INS / SLP / MAT codes."),
                Btn("Btn_FloorTag_V001", "Floor Tag", "FloorTag.V001.FloorTagCommand",
                    "RoofTag", ToolCatalog.FloorTag,
                    "Pick a floor: places spot elevations on its top face at the shape points (or points you pick), skipping duplicates."),
                Btn("Btn_FloorPointComparison_V001", "Comparison", "FloorPointComparison.V001.Commands.FloorComparisonCommand",
                    "PointComparison", ToolCatalog.FloorPointComparison,
                    "Pick floor A and floor B: compares their shape-point heights point by point. Reads the floors only."),
                Btn("Btn_FloorPointElevationSync_V001", "Elevation Sync", "FloorPointElevationSync.V001.Command",
                    "ElevationSync", ToolCatalog.FloorPointElevationSync,
                    "Pick floor A and floor B: matches their shape points by plan position and sets the chosen heights. One Undo."),
                // Added in plugin 6.37.0.
                Btn("Btn_FloorTaperedEpsPattern_V001", "Floor EPS", "FloorTaperedEpsPattern.V001.Commands.TaperedEpsPattern2DCommand",
                    "TaperedEpsPattern2D", ToolCatalog.FloorTaperedEpsPattern,
                    "In a plan view, pick a floor with a single outer loop: draws the tapered EPS insulation layout as a 2D detail pattern. Reads the floor only."),
            };

            RibbonLayoutHelper.AddStackedButtons(panel, items);
        }

        /// <summary>
        /// Creates the "Floor Views" panel on the Documents tab (plugin 6.37.0), right after View Create: Floor Edge
        /// Around Sections, Floor Edge Element Sections and Floor View Focus as one stack of three plain buttons
        /// (one version each). They reuse the roof view-tool icons.
        /// </summary>
        public static void BuildViewsPanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Views");
            ApplyFloorPanelColor(panel);
            const string viewIcons = "Revit26_Plugin_Revised.Resources.Icons.ViewTools.";

            var aroundEdges = new PushButtonData("Btn_FloorEdgeAroundSections_V001", "Floor Around Edges", assemblyPath,
                "Revit26_Plugin_Revised.FloorEdgeAroundSections.V001.FloorEdgeAroundSectionsCommand")
            {
                Image = ImageUtils.Load(viewIcons + "RoofEdgeAroundSections_V005_16.png"),
                ToolTip = ToolCatalog.FloorEdgeAroundSections.Tip(
                    "Select floors first: creates section views around every edge of the selected floors.")
            };
            var edgeElement = new PushButtonData("Btn_FloorEdgeElementSections_V001", "Floor Edge Element", assemblyPath,
                "Revit26_Plugin_Revised.FloorEdgeElementSections.V001.FloorEdgeElementSectionsCommand")
            {
                Image = ImageUtils.Load(viewIcons + "RoofEdgeElementSections_V002_16.png"),
                ToolTip = ToolCatalog.FloorEdgeElementSections.Tip(
                    "Pick floors in the window: creates section views where linked elements meet the selected floor edges.")
            };
            var focus = new PushButtonData("Btn_FloorViewFocus_V001", "Floor Focus", assemblyPath,
                "Revit26_Plugin_Revised.FloorViewFocus.V001.Commands.FloorViewFocusCommand")
            {
                Image = ImageUtils.Load(viewIcons + "RoofViewFocus_16.png"),
                ToolTip = ToolCatalog.FloorViewFocus.Tip(
                    "In a plan view, select floors first: crops the plan view to the selected floors.")
            };

            RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData> { aroundEdges, edgeElement, focus });
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
