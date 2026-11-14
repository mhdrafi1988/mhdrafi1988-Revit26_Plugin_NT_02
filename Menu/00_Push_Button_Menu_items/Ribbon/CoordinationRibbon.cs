using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Coordination panel of the SloperPro Documents 2 tab.</summary>
    public static class CoordinationRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Coordination.";

        /// <summary>Creates the Coordination panel on <paramref name="tabName"/> with the Void Openings and Base Level to Parameter buttons.</summary>
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
                    "model (the active model or a Revit link) and their categories, and see how many elements each has. " +
                    "Clash analysis and void placement follow in later versions.")
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
                    "and can create it as a shared instance Text parameter.")
            };
            panel.AddItem(baseLevelToParameter);
        }
    }
}
