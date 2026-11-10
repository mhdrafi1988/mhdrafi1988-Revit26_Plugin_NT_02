using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Infrastructure;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Tagging panel of the SloperPro Documents tab.</summary>
    public static class TaggingRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Tagging.";

        /// <summary>Creates the Tagging panel on <paramref name="tabName"/> with the Tag To Text, Align Spot Elevations, Align to Detail Line and Distribute buttons, three quick-align split buttons (Vertical, Horizontal, to Tag) and two quick-distribute buttons.</summary>
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
                    "at the same offset from its point, or slides them onto picked detail lines. Can also align the leader " +
                    "end and bend points. Distances are paper mm.")
            };
            panel.AddItem(align);

            var alignToLine = new PushButtonData("Btn_AlignSpotElevations_ToDetailLine_V001", "Align to\nDetail Line", assemblyPath,
                "Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevationsToDetailLineCommand")
            {
                Image = ImageUtils.Load(Icons + "AlignSpotElevationsDetailLine_16.png"),
                LargeImage = ImageUtils.Load(Icons + "AlignSpotElevationsDetailLine_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(
                    "Slides the selected spot elevations onto detail lines you pick: one optional line for the bend points, " +
                    "one for the leader end points and one for the text. The bend and end points can instead line up with " +
                    "a spot elevation you click (Pick tag…). Checks for each line whether a vertical or " +
                    "horizontal move can reach it and does what is possible; spots past the end of a line are skipped " +
                    "unless you choose to extend it. Select one detail line with the spots to use it for the end points. " +
                    "One Undo step.")
            };
            panel.AddItem(alignToLine);

            var distribute = new PushButtonData("Btn_AlignSpotElevations_Distribute_V001", "Distribute\nSpot Elev.", assemblyPath,
                "Revit26_Plugin_Revised.AlignSpotElevations.V001.Commands.AlignSpotElevationsDistributeCommand")
            {
                Image = ImageUtils.Load(Icons + "AlignSpotElevationsDistribute_16.png"),
                LargeImage = ImageUtils.Load(Icons + "AlignSpotElevationsDistribute_32.png"),
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(
                    "Spaces the selected spot elevations along their column or row without lining them up: evenly " +
                    "(first and last stay) or at a fixed gap in paper mm (top / left stays). Works on the text, the " +
                    "leader end points and the bend points. Opens the Align Spot Elevations window in Distribute only. " +
                    "One Undo step.")
            };
            panel.AddItem(distribute);

            // Quick align: same tool, no window, last-used options. Three small split buttons grouped by direction.
            // Revit runs the item last picked in each split button on its main click (kept for the Revit session).
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

            // Text follows a moved end or bend point, as in Revit.
            var endsVertical = QuickPart(assemblyPath, "QuickEndsVertical", "Quick Align Ends Vertical", "EndsVertical",
                "Lines the leader end points of the selected spot elevations up in one column right away. The text " +
                "moves with each end. A leader with a bend only lets its end slide along the bend-to-end segment. " +
                "Uses the line position and spacing last used in the window. One Undo step.");
            var endsHorizontal = QuickPart(assemblyPath, "QuickEndsHorizontal", "Quick Align Ends Horizontal", "EndsHorizontal",
                "Lines the leader end points of the selected spot elevations up in one row right away. The text " +
                "moves with each end. A leader with a bend only lets its end slide along the bend-to-end segment. " +
                "Uses the line position and spacing last used in the window. One Undo step.");
            var bendsVertical = QuickPart(assemblyPath, "QuickBendsVertical", "Quick Align Bends Vertical", "BendsVertical",
                "Lines the leader bend points (shoulders) of the selected spot elevations up in one column right away. " +
                "The leader end and text move with each bend. Uses the line position and spacing last used in the " +
                "window. One Undo step.");
            var bendsHorizontal = QuickPart(assemblyPath, "QuickBendsHorizontal", "Quick Align Bends Horizontal", "BendsHorizontal",
                "Lines the leader bend points (shoulders) of the selected spot elevations up in one row right away. " +
                "The leader end and text move with each bend. Uses the line position and spacing last used in the " +
                "window. One Undo step.");

            // Quick align to a tag: click one spot elevation; the parts line up with its point.
            var endsToTag = QuickPart(assemblyPath, "QuickEndsToTag", "Quick Align Ends to Tag", "EndsToTag",
                "Lines up the leader end points of the selected spot elevations with the end point of one spot " +
                "elevation you click, right away. The text moves with each end. Uses the line direction (vertical " +
                "or horizontal) last used in the window. One Undo step.");
            var bendsToTag = QuickPart(assemblyPath, "QuickBendsToTag", "Quick Align Bends to Tag", "BendsToTag",
                "Lines up the leader bend points (shoulders) of the selected spot elevations with the bend point of " +
                "one spot elevation you click, right away. The leader end and text move with each bend. Uses the " +
                "line direction (vertical or horizontal) last used in the window. One Undo step.");

            var splitVertical = QuickSplit(QuickSplitMemory.SplitVertical, "Quick Align Vertical", quickVertical,
                "Quick align in one column: tags, leader ends or leader bends. The button runs the one you used last.");
            var splitHorizontal = QuickSplit(QuickSplitMemory.SplitHorizontal, "Quick Align Horizontal", quickHorizontal,
                "Quick align in one row: tags, leader ends or leader bends. The button runs the one you used last.");
            var splitToTag = QuickSplit(QuickSplitMemory.SplitToTag, "Quick Align to Tag", endsToTag,
                "Quick align to a spot elevation you click: leader ends or leader bends. The button runs the one you used last.");

            IList<RibbonItem> created = RibbonLayoutHelper.AddStackedButtons(panel,
                new RibbonItemData[] { splitVertical, splitHorizontal, splitToTag });
            AddSplitItems(created, splitVertical.Name, quickVertical, endsVertical, bendsVertical);
            AddSplitItems(created, splitHorizontal.Name, quickHorizontal, endsHorizontal, bendsHorizontal);
            AddSplitItems(created, splitToTag.Name, endsToTag, bendsToTag);

            // Quick distribute: no window, parts and spacing last used in the window (Keep current spacing = even).
            var distributeVertical = QuickPart(assemblyPath, "QuickDistributeVertical", "Quick Distribute Vertical", "DistributeVertical",
                "Spaces the selected spot elevations up-down along their column right away; they keep their left-right " +
                "position. Uses the parts and spacing (even or fixed gap) last used in the Align Spot Elevations window. " +
                "One Undo step.");
            var distributeHorizontal = QuickPart(assemblyPath, "QuickDistributeHorizontal", "Quick Distribute Horizontal", "DistributeHorizontal",
                "Spaces the selected spot elevations left-right along their row right away; they keep their up-down " +
                "position. Uses the parts and spacing (even or fixed gap) last used in the Align Spot Elevations window. " +
                "One Undo step.");
            RibbonLayoutHelper.AddStackedButtons(panel, new RibbonItemData[] { distributeVertical, distributeHorizontal });
        }

        /// <summary>Creates a small split button that shows <paramref name="primary"/>'s icon until the user picks another item.</summary>
        private static SplitButtonData QuickSplit(string name, string label, PushButtonData primary, string tip)
            => new SplitButtonData(name, label)
            {
                Image = primary.Image,
                LargeImage = primary.LargeImage,
                ToolTip = ToolCatalog.AlignSpotElevations.Tip(tip)
            };

        /// <summary>Adds <paramref name="items"/> to the split button called <paramref name="name"/>; the item used last (saved in <see cref="QuickSplitMemory"/>) becomes the current one, else the first stays the default.</summary>
        private static void AddSplitItems(IList<RibbonItem> created, string name, params PushButtonData[] items)
        {
            var split = created.OfType<SplitButton>().FirstOrDefault(s => s.Name == name);
            if (split == null) return;
            var added = items.Select(split.AddPushButton).ToList();
            split.IsSynchronizedWithCurrentItem = true;

            // Start on the item used last, also across Revit restarts (see QuickSplitMemory).
            string last = QuickSplitMemory.GetLast(name);
            PushButton remembered = last == null ? null : added.FirstOrDefault(b => b.Name == last);
            if (remembered != null) split.CurrentButton = remembered;
        }

        /// <summary>One small quick-align or quick-distribute button of Align Spot Elevations.</summary>
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
