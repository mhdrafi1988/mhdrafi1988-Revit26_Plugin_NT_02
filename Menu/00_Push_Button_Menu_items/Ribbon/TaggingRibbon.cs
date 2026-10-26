using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Tagging panel of the SloperPro Documents tab.</summary>
    public static class TaggingRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Tagging.";

        /// <summary>Creates the Tagging panel on <paramref name="tabName"/> with the Tag To Text button.</summary>
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
        }
    }
}
