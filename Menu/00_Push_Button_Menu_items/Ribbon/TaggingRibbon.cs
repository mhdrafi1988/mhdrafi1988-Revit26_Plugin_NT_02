using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Tagging panel of the SloperPro Documents tab.</summary>
    public static class TaggingRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Tagging.";

        /// <summary>Creates the Tagging panel on <paramref name="tabName"/> with the Tag To Text and Align Spot Elevations buttons (plus the two quick-align buttons).</summary>
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

            var align = new PushButtonData("Btn_AlignSpotElevations_V001", "Align Spot\nElevations", assemblyPath,
                "Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevationsCommand")
            {
                Image = ImageUtils.Load(Icons + "AlignSpotElevations_16.png"),
                LargeImage = ImageUtils.Load(Icons + "AlignSpotElevations_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(
                    "Lines up the tags of the selected spot elevations in one column or one row (average, most common, " +
                    "median, min / max or a reference tag), spaces them evenly or at a fixed gap, or places each tag " +
                    "at the same offset from its point. Lines up the text, the leader ends or the shoulders. " +
                    "Distances are paper mm. The measured points never move.")
            };
            panel.AddItem(align);

            // Quick align: same tool, no window, last-used options. Stacked small buttons, not a dropdown
            // (dropdowns are only for grouping different tools).
            var quickVertical = new PushButtonData("Btn_AlignSpotElevations_QuickVertical_V001", "Quick Align Vertical",
                assemblyPath, "Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevationsQuickVerticalCommand")
            {
                Image = ImageUtils.Load(Icons + "AlignSpotElevationsVertical_16.png"),
                LargeImage = ImageUtils.Load(Icons + "AlignSpotElevationsVertical_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(
                    "Lines the selected spot elevation tags up in one column right away, with the line position and " +
                    "spacing last used in the Align Spot Elevations window. One Undo step.")
            };
            var quickHorizontal = new PushButtonData("Btn_AlignSpotElevations_QuickHorizontal_V001", "Quick Align Horizontal",
                assemblyPath, "Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevationsQuickHorizontalCommand")
            {
                Image = ImageUtils.Load(Icons + "AlignSpotElevationsHorizontal_16.png"),
                LargeImage = ImageUtils.Load(Icons + "AlignSpotElevationsHorizontal_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(
                    "Lines the selected spot elevation tags up in one row right away, with the line position and " +
                    "spacing last used in the Align Spot Elevations window. One Undo step.")
            };
            RibbonLayoutHelper.AddStackedButtons(panel, new RibbonItemData[] { quickVertical, quickHorizontal });
        }
    }
}
