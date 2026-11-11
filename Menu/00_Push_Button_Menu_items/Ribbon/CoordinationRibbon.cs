using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Coordination panel of the SloperPro Documents 2 tab.</summary>
    public static class CoordinationRibbon
    {
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.Coordination.";

        /// <summary>Creates the Coordination panel on <paramref name="tabName"/> with the Void Openings button.</summary>
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Coordination");

            var voidOpenings = new PushButtonData("Btn_VoidOpenings_V001", "Void\nOpenings", assemblyPath,
                "Revit26_Plugin_Revised.VoidOpenings.V001.Commands.VoidOpeningsCommand")
            {
                Image = ImageUtils.Load(Icons + "VoidOpenings_16.png"),
                LargeImage = ImageUtils.Load(Icons + "VoidOpenings_32.png"),
                ToolTip = ToolCatalog.VoidOpenings.Tip(
                    "Finds where MEP passes through structure, sizes each opening from your clearance rules, places a " +
                    "void family with a Job ID and cuts the structure. Works on one structural and one MEP model at a time " +
                    "(the active model or a Revit link). For a linked structural model it writes an opening package to " +
                    "apply inside the structural file.")
            };
            panel.AddItem(voidOpenings);
        }
    }
}
