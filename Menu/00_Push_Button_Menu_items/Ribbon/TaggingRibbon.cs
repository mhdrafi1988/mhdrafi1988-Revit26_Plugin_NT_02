using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Tagging panel of the SloperPro Documents tab.</summary>
    public static class TaggingRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Tagging.";

        /// <summary>Creates the Tagging panel on <paramref name="tabName"/> with the Tag To Text and Align Spot Elevations buttons (plus the six quick-align buttons).</summary>
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
                    "at the same offset from its point. Can also align the leader end and bend points. Distances are paper mm.")
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
                    "Lines the selected spot elevation tags up in one column right away, with the parts, line position and " +
                    "spacing last used in the Align Spot Elevations window. One Undo step.")
            };
            var quickHorizontal = new PushButtonData("Btn_AlignSpotElevations_QuickHorizontal_V001", "Quick Align Horizontal",
                assemblyPath, "Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevationsQuickHorizontalCommand")
            {
                Image = ImageUtils.Load(Icons + "AlignSpotElevationsHorizontal_16.png"),
                LargeImage = ImageUtils.Load(Icons + "AlignSpotElevationsHorizontal_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(
                    "Lines the selected spot elevation tags up in one row right away, with the parts, line position and " +
                    "spacing last used in the Align Spot Elevations window. One Undo step.")
            };
            RibbonLayoutHelper.AddStackedButtons(panel, new RibbonItemData[] { quickVertical, quickHorizontal });

            // Quick align of one leader part. Text follows a moved end or bend point, as in Revit.
            RibbonLayoutHelper.AddStackedButtons(panel, new RibbonItemData[]
            {
                QuickPart(assemblyPath, "QuickEndsVertical", "Quick Align Ends Vertical", "EndsVertical",
                    "Lines the leader end points of the selected spot elevations up in one column right away. The text " +
                    "moves with each end. A leader with a bend only lets its end slide along the bend-to-end segment. " +
                    "Uses the line position and spacing last used in the window. One Undo step."),
                QuickPart(assemblyPath, "QuickEndsHorizontal", "Quick Align Ends Horizontal", "EndsHorizontal",
                    "Lines the leader end points of the selected spot elevations up in one row right away. The text " +
                    "moves with each end. A leader with a bend only lets its end slide along the bend-to-end segment. " +
                    "Uses the line position and spacing last used in the window. One Undo step.")
            });
            RibbonLayoutHelper.AddStackedButtons(panel, new RibbonItemData[]
            {
                QuickPart(assemblyPath, "QuickBendsVertical", "Quick Align Bends Vertical", "BendsVertical",
                    "Lines the leader bend points (shoulders) of the selected spot elevations up in one column right away. " +
                    "The leader end and text move with each bend. Uses the line position and spacing last used in the " +
                    "window. One Undo step."),
                QuickPart(assemblyPath, "QuickBendsHorizontal", "Quick Align Bends Horizontal", "BendsHorizontal",
                    "Lines the leader bend points (shoulders) of the selected spot elevations up in one row right away. " +
                    "The leader end and text move with each bend. Uses the line position and spacing last used in the " +
                    "window. One Undo step.")
            });
        }

        /// <summary>One small quick-align button for a leader part of Align Spot Elevations.</summary>
        private static PushButtonData QuickPart(string assemblyPath, string key, string label, string icon, string tip)
            => new PushButtonData($"Btn_AlignSpotElevations_{key}_V001", label, assemblyPath,
                $"Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevations{key}Command")
            {
                Image = ImageUtils.Load(Icons + $"AlignSpotElevations{icon}_16.png"),
                LargeImage = ImageUtils.Load(Icons + $"AlignSpotElevations{icon}_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(tip)
            };
    }
}
