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
        /// <summary>Floor Tools icon folder (embedded resources), used by the Floor Slope buttons since 6.31.1.</summary>
        private const string FloorIcons = "Revit26_Plugin_Revised.Resources.Icons.FloorTools.";

        /// <summary>
        /// Creates the "Floor Slope" panel on <paramref name="tabName"/>: Floor Slope By Drain and
        /// Floor Slope By Point as two stacked buttons (one version each, so no pulldown).
        /// </summary>
        public static void BuildSlopePanel(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Floor Slope");

            var byDrain = new PushButtonData("Btn_FloorSlopeByDrain_V001", "By Drain", assemblyPath,
                "Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands.FloorSlopeByDrainCommand")
            {
                Image = ImageUtils.Load(FloorIcons + "FloorSlopeByDrain_16.png"),
                LargeImage = ImageUtils.Load(FloorIcons + "FloorSlopeByDrain_32.png"),
                ToolTip = ToolCatalog.FloorSlopeByDrain.Tip(
                    "Pick one or more floors and slope each one's shape points down to its drains: openings in the floor sketch " +
                    "and drain families (Plumbing Fixtures) on the floor. Asks before sloping a floor whose type has no Variable layer. One Undo.")
            };
            var byPoint = new PushButtonData("Btn_FloorSlopeByPoint_V001", "By Point", assemblyPath,
                "Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Commands.FloorSlopeByPointCommand")
            {
                Image = ImageUtils.Load(FloorIcons + "FloorSlopeByPoint_16.png"),
                LargeImage = ImageUtils.Load(FloorIcons + "FloorSlopeByPoint_32.png"),
                ToolTip = ToolCatalog.FloorSlopeByPoint.Tip(
                    "Pick a floor, then click the drain points on it; the floor's shape points are sloped down to them, " +
                    "with optional ridge lines between drain groups. Asks before sloping a floor whose type has no Variable layer.")
            };

            RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData> { byDrain, byPoint });
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
