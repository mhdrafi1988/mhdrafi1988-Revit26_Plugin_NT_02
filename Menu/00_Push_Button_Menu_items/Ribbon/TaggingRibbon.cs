using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Tagging panel of the SloperPro Documents tab.</summary>
    public static class TaggingRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Tagging.";
        private const string RoofIcons = "Revit26_Plugin_Revised.Resources.Icons.RoofTools.";

        /// <summary>
        /// Creates the Tagging panel on <paramref name="tabName"/>: Tag To Text, then the Roof Tag
        /// dropdown (V018, then V017), as two large buttons side by side.
        /// </summary>
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Tagging");

            var tagToText = new PushButtonData("Btn_TagToText_V001", "Tag To Text", assemblyPath,
                "Revit26_Plugin_Revised.TagToText.V001.Commands.TagToTextCommand")
            {
                Image = ImageUtils.Load(Icons + "TagToText_16.png"),
                LargeImage = ImageUtils.Load(Icons + "TagToText_32.png"),
                ToolTip = ToolCatalog.TagToText.Tip(
                    "Copies spot elevations, spot coordinates, spot slopes and tags into text notes with leaders, " +
                    "at the same place, in this view or a new duplicate of it (with or without detailing).")
            };
            panel.AddItem(tagToText);

            // Roof Tag moved here from the Roof Tools panel (Modelling tab) in plugin 7.0.0.
            // Internal button and pulldown names are unchanged.
            var roofTagV017 = new PushButtonData("Btn_RoofTagCommand_V017", "Roof Tag V017", assemblyPath,
                "Revit26_Plugin_Revised.RoofTag.V017.RoofTagCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "RoofTag_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Roof Tag", "V017", "V016 with the standard three-zone window layout.")
            };
            var roofTagV018 = new PushButtonData("Btn_RoofTagCommand_V018", "Roof Tag V018", assemblyPath,
                "Revit26_Plugin_Revised.RoofTag.V018.RoofTagCommand")
            {
                Image = ImageUtils.Load(RoofIcons + "RoofTag_16.png"),
                ToolTip = RibbonLayoutHelper.VersionTip("Roof Tag", "V018", "V017 on the shared ToolWindowShell (log in body).")
            };

            // Highest version first; V018 supplies the dropdown's icon.
            PulldownButtonData roofTagPulldown = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofTag", "Roof Tag", roofTagV018);
            roofTagPulldown.LargeImage = ImageUtils.Load(RoofIcons + "RoofTag_32.png");
            var roofTagItem = panel.AddItem(roofTagPulldown);
            RibbonLayoutHelper.WirePulldownButton(new List<RibbonItem> { roofTagItem }, "Pulldown_RoofTag",
                roofTagV018, roofTagV017);
        }
    }
}
