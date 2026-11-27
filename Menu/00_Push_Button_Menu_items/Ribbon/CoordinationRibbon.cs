using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Coordination panel of the SloperPro Documents 2 tab.</summary>
    public static class CoordinationRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Coordination.";

        /// <summary>Creates the Coordination panel on <paramref name="tabName"/> with the Void Openings, Base Level to Parameter and Base and Nearest Level buttons.</summary>
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Coordination");

            var voidOpenings = new PushButtonData("Btn_VoidOpenings_V001", "Void\nOpenings", assemblyPath,
                "Revit26_Plugin_Revised.VoidOpenings.V001.Commands.VoidOpeningsCommand")
            {
                Image = ImageUtils.Load(Icons + "VoidOpenings_16.png"),
                LargeImage = ImageUtils.Load(Icons + "VoidOpenings_32.png"),
                ToolTip = ToolCatalog.VoidOpenings.Tip(
                    "Openings for MEP through structure. In this version: choose the structural model and the MEP " +
                    "model (the active model or a Revit link) and their categories, set the opening clearance rules, " +
                    "and Analyze: find where MEP passes through structure, see the opening sized for each place, " +
                    "zoom to it and approve or reject it. Nothing in the model is changed; void placement follows in a later version.")
            };
            panel.AddItem(voidOpenings);

            var baseLevelToParameter = new PushButtonData("Btn_BaseLevelToParameter_V001", "Base Level to\nParameter", assemblyPath,
                "Revit26_Plugin_Revised.BaseLevelToParameter.V001.Commands.BaseLevelToParameterCommand")
            {
                Image = ImageUtils.Load(Icons + "BaseLevelToParameter_16.png"),
                LargeImage = ImageUtils.Load(Icons + "BaseLevelToParameter_32.png"),
                ToolTip = ToolCatalog.BaseLevelToParameter.Tip(
                    "Reads each element's base level and offset and writes them to a text parameter as \"Level 02 (+450)\". " +
                    "Lists the active view's model elements (or the whole host model), checks the target parameter first, " +
                    "and can create it as a shared instance Text parameter. " +
                    "Values already there can be skipped, rewritten, or cleared so only the new values remain.")
            };
            panel.AddItem(baseLevelToParameter);

            var baseAndNearestLevel = new PushButtonData("Btn_BaseAndNearestLevel_V001", "Base and\nNearest Level", assemblyPath,
                "Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Commands.BaseAndNearestLevelCommand")
            {
                Image = ImageUtils.Load(Icons + "BaseAndNearestLevel_16.png"),
                LargeImage = ImageUtils.Load(Icons + "BaseAndNearestLevel_32.png"),
                ToolTip = ToolCatalog.BaseAndNearestLevel.Tip(
                    "Roofs, floors and walls only. Reads each element's base level and offset, finds the project level " +
                    "nearest its real base elevation, and writes four parameters: base level name, base offset, nearest " +
                    "level name, nearest offset. Offsets are Length parameters, so schedules sort and total them. " +
                    "A separate action re-hosts elements onto their nearest level without moving them.")
            };
            panel.AddItem(baseAndNearestLevel);
        }
    }
}
