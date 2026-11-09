using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    public static class ViewToolsRibbon
    {
        // Every View Tools button is a pulldown: highest version number first,
        // every older version below it in descending order. Each tool lists only
        // its latest two versions.
        private const string Icons = "Revit26_Plugin_Revised.Resources.Icons.ViewTools.";
        private const string Rebuilt = " with the standard three-zone window layout.";
        private const string OnShell = " on the shared ToolWindowShell (log in body, capped grids).";

        private static PushButtonData Btn(string assemblyPath, string name, string text, string commandClass,
                                          string icon, string tool, string version, string detail = null)
        {
            return new PushButtonData(name, text, assemblyPath, commandClass)
            {
                Image = ImageUtils.Load(Icons + icon),
                ToolTip = RibbonLayoutHelper.VersionTip(tool, version, detail)
            };
        }

        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            RibbonPanel createPanel = app.CreateRibbonPanel(tabName, "View Create");

            var sectionsFromLinesV012 = Btn(assemblyPath, "Btn_SectionsFromDetailLines_V012", "Sections From Lines V012", "Revit26_Plugin_Revised.CreateSections.V012.Commands.CreateSectionsFromDetailLines",
                "SectionsFromDetailLines_16.png", "Create Sections From Detail Lines", "V012", "V011" + Rebuilt);
            var sectionsFromLinesV013 = Btn(assemblyPath, "Btn_SectionsFromDetailLines_V013", "Sections From Lines V013", "Revit26_Plugin_Revised.CreateSections.V013.Commands.CreateSectionsFromDetailLines",
                "SectionsFromDetailLines_16.png", "Create Sections From Detail Lines", "V013", "V012" + OnShell);

            var edgeAroundV006 = Btn(assemblyPath, "Btn_RoofEdgeAroundSections_V006", "Section Around Edges V006", "Revit26_Plugin_Revised.RoofEdgeAroundSections.V006.RoofEdgeAroundSectionsCommand",
                "RoofEdgeAroundSections_V005_16.png", "Roof Edge Around Sections", "V006", "V005" + Rebuilt);
            var edgeAroundV007 = Btn(assemblyPath, "Btn_RoofEdgeAroundSections_V007", "Section Around Edges V007", "Revit26_Plugin_Revised.RoofEdgeAroundSections.V007.RoofEdgeAroundSectionsCommand",
                "RoofEdgeAroundSections_V005_16.png", "Roof Edge Around Sections", "V007", "V006" + OnShell);

            var edgeElementV003 = Btn(assemblyPath, "Btn_RoofEdgeElementSections_V003", "Edge Element V003", "Revit26_Plugin_Revised.RoofEdgeElementSections.V003.RoofEdgeElementSectionsCommand",
                "RoofEdgeElementSections_V002_16.png", "Roof Edge Element Sections", "V003", "V002" + Rebuilt);
            var edgeElementV004 = Btn(assemblyPath, "Btn_RoofEdgeElementSections_V004", "Edge Element V004", "Revit26_Plugin_Revised.RoofEdgeElementSections.V004.RoofEdgeElementSectionsCommand",
                "RoofEdgeElementSections_V002_16.png", "Roof Edge Element Sections", "V004", "V003" + OnShell);

            var roofViewFocusV003 = Btn(assemblyPath, "Btn_RoofViewFocus_V003", "Roof View Focus V003", "Revit26_Plugin_Revised.RoofViewFocus.V003.Commands.RoofViewFocusCommand",
                "RoofViewFocus_16.png", "Roof View Focus", "V003", "V002" + Rebuilt);
            var roofViewFocusV004 = Btn(assemblyPath, "Btn_RoofViewFocus_V004", "Roof View Focus V004", "Revit26_Plugin_Revised.RoofViewFocus.V004.Commands.RoofViewFocusCommand",
                "RoofViewFocus_16.png", "Roof View Focus", "V004", "V003" + OnShell);


            // One version only, so a plain large button (not a pulldown).
            var isolatedSectionV001 = Btn(assemblyPath, "Btn_IsolatedSection_V001", "Isolated\nSection", "Revit26_Plugin_Revised.IsolatedSection.V001.Commands.IsolatedSectionCommand",
                "IsolatedSection_16.png", "Isolated Section", "V001");
            isolatedSectionV001.LargeImage = ImageUtils.Load(Icons + "IsolatedSection_32.png");
            isolatedSectionV001.ToolTip = Revit26_Plugin_Revised.Shared.Services.ToolCatalog.IsolatedSection.Tip(
                "Makes one 3D view of the selected elements (host or linked): the section box is turned to be " +
                "parallel to the largest side of any of them (or a side or face you choose), the camera faces " +
                "that side, and the elements are isolated (temporary by default).");
            // Slide-out (plugin 6.13.0): Roof Focus and Isolated Section sit inside the panel's arrow.
            // This call comes after isolatedSectionV001 is defined above it.
            var createItems = RibbonLayoutHelper.AddStackedButtonsWithSlideOut(createPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_SectionsFromDetailLines", "From Lines", sectionsFromLinesV013),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofEdgeAroundSections", "Around Edges", edgeAroundV007),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofEdgeElementSections", "Edge Element", edgeElementV004),
            }, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RoofViewFocus", "Roof Focus", roofViewFocusV004),
                isolatedSectionV001,
            });
            RibbonLayoutHelper.WirePulldownButton(createItems, "Pulldown_SectionsFromDetailLines", sectionsFromLinesV013, sectionsFromLinesV012);
            RibbonLayoutHelper.WirePulldownButton(createItems, "Pulldown_RoofEdgeAroundSections", edgeAroundV007, edgeAroundV006);
            RibbonLayoutHelper.WirePulldownButton(createItems, "Pulldown_RoofEdgeElementSections", edgeElementV004, edgeElementV003);
            RibbonLayoutHelper.WirePulldownButton(createItems, "Pulldown_RoofViewFocus", roofViewFocusV004, roofViewFocusV003);

            RibbonPanel placePanel = app.CreateRibbonPanel(tabName, "View Place");

            var placeSectionsV323 = Btn(assemblyPath, "Btn_AutoPlaceSectionsCommand_V323", "Place Sections V323", "Revit26_Plugin_Revised.APUS.V323.Commands.AutoPlaceSectionsCommand",
                "AutoPlaceSections_16.png", "Auto Place Sections", "V323", "V322" + Rebuilt);
            var placeSectionsV324 = Btn(assemblyPath, "Btn_AutoPlaceSectionsCommand_V324", "Place Sections V324", "Revit26_Plugin_Revised.APUS.V324.Commands.AutoPlaceSectionsCommand",
                "AutoPlaceSections_16.png", "Auto Place Sections", "V324", "V323" + OnShell);
            var calloutV020 = Btn(assemblyPath, "Btn_CalloutToSectionViewPlacement_V020", "Callout To Section V020", "Revit26_Plugin_Revised.CalloutCOP.V020.Commands.CalloutCOPCommand",
                "CalloutToSection_16.png", "Callout To Section View Placement", "V020", "V019" + Rebuilt);
            var calloutV021 = Btn(assemblyPath, "Btn_CalloutToSectionViewPlacement_V021", "Callout To Section V021", "Revit26_Plugin_Revised.CalloutCOP.V021.Commands.CalloutCOPCommand",
                "CalloutToSection_16.png", "Callout To Section View Placement", "V021", "V020" + OnShell);
            var headPlacerV014 = Btn(assemblyPath, "Btn_RefSectionHeadPlacerCommand_V014", "Section Head Placer V014", "Revit26_Plugin_Revised.RefSectionHeadPlacer.V014.Commands.RefSectionHeadPlacerCommand",
                "RefSectionHeadPlacer_16.png", "Reference Section Head Placer", "V014", "V013" + Rebuilt);
            var headPlacerV015 = Btn(assemblyPath, "Btn_RefSectionHeadPlacerCommand_V015", "Section Head Placer V015", "Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Commands.RefSectionHeadPlacerCommand",
                "RefSectionHeadPlacer_16.png", "Reference Section Head Placer", "V015", "V014" + OnShell);
            var taggerV005 = Btn(assemblyPath, "Btn_SectionViewAutoTagger.V005", "Section View Tagger V005", "Revit26_Plugin_Revised.SectionViewAutoTagger.V005.SectionViewAutoTaggerCommand",
                "SectionAutoTagger_16.png", "Section View Auto Tagger", "V005", "V004" + Rebuilt);
            var taggerV006 = Btn(assemblyPath, "Btn_SectionViewAutoTagger.V006", "Section View Tagger V006", "Revit26_Plugin_Revised.SectionViewAutoTagger.V006.SectionViewAutoTaggerCommand",
                "SectionAutoTagger_16.png", "Section View Auto Tagger", "V006", "V005" + OnShell);

            var placeItems = RibbonLayoutHelper.AddStackedButtons(placePanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_AutoPlaceSections", "Place Sections", placeSectionsV324),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_CalloutToSection", "Callout To Section", calloutV021),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_RefSectionHeadPlacer", "Head Placer", headPlacerV015),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_SectionViewAutoTagger", "View Tagger", taggerV006),
            });
            RibbonLayoutHelper.WirePulldownButton(placeItems, "Pulldown_AutoPlaceSections", placeSectionsV324, placeSectionsV323);
            RibbonLayoutHelper.WirePulldownButton(placeItems, "Pulldown_CalloutToSection", calloutV021, calloutV020);
            RibbonLayoutHelper.WirePulldownButton(placeItems, "Pulldown_RefSectionHeadPlacer", headPlacerV015, headPlacerV014);
            RibbonLayoutHelper.WirePulldownButton(placeItems, "Pulldown_SectionViewAutoTagger", taggerV006, taggerV005);

            RibbonPanel renamePanel = app.CreateRibbonPanel(tabName, "View Rename");

            var bubbleV007 = Btn(assemblyPath, "Btn_BubbleAutoRenumber_V007", "Bubble Renumber V007", "Revit26_Plugin_Revised.BubbleAutoRenumber.V007.Commands.SectionAutoRenumberCommand",
                "BubbleAutoRenumber_16.png", "Bubble Auto Renumber", "V007", "V006" + Rebuilt);
            var bubbleV008 = Btn(assemblyPath, "Btn_BubbleAutoRenumber_V008", "Bubble Renumber V008", "Revit26_Plugin_Revised.BubbleAutoRenumber.V008.Commands.SectionAutoRenumberCommand",
                "BubbleAutoRenumber_16.png", "Bubble Auto Renumber", "V008", "V007" + OnShell);
            var bubbleV009 = Btn(assemblyPath, "Btn_BubbleAutoRenumber_V009", "Bubble Renumber V009", "Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Commands.BubbleAutoRenumberCommand",
                "BubbleAutoRenumber_16.png", "Bubble Auto Renumber", "V009", "V008 with the bubble renumber command renamed.");
            var sectionRenamerV025 = Btn(assemblyPath, "Btn_SectionAutoRenamer_V025", "Section Renamer V025", "Revit26_Plugin_Revised.SectionAutoRenamer.V025.Commands.OpenSectionManagerCommand",
                "SectionAutoRenamer_16.png", "Section Auto Renamer", "V025", "V024" + Rebuilt);
            var sectionRenamerV026 = Btn(assemblyPath, "Btn_SectionAutoRenamer_V026", "Section Renamer V026", "Revit26_Plugin_Revised.SectionAutoRenamer.V026.Commands.OpenSectionManagerCommand",
                "SectionAutoRenamer_16.png", "Section Auto Renamer", "V026", "V025" + OnShell);
            var autoRenamerV005 = Btn(assemblyPath, "Btn_ViewAutoRenamer_V005", "View Renamer V005", "Revit26_Plugin_Revised.ViewAutoRenamer.V005.Commands.OpenViewAutoRenamerCommand",
                "ViewAutoRenamer_V004_16.png", "View Auto Renamer", "V005", "V004" + Rebuilt);
            var autoRenamerV006 = Btn(assemblyPath, "Btn_ViewAutoRenamer_V006", "View Renamer V006", "Revit26_Plugin_Revised.ViewAutoRenamer.V006.Commands.OpenViewAutoRenamerCommand",
                "ViewAutoRenamer_V004_16.png", "View Auto Renamer", "V006", "V005" + OnShell);
            var autoRenamerV007 = Btn(assemblyPath, "Btn_ViewAutoRenamer_V007", "View Renamer V007", "Revit26_Plugin_Revised.ViewAutoRenamer.V007.Commands.OpenViewAutoRenamerCommand",
                "ViewAutoRenamer_V004_16.png", "View Auto Renamer", "V007", "V006, opens faster on large projects.");

            var renameItems = RibbonLayoutHelper.AddStackedButtons(renamePanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_BubbleAutoRenumber", "Bubble Renumber", bubbleV009),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_SectionAutoRenamer", "Section Renamer", sectionRenamerV026),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_ViewAutoRenamer", "Auto Renamer", autoRenamerV007),
            });
            RibbonLayoutHelper.WirePulldownButton(renameItems, "Pulldown_BubbleAutoRenumber", bubbleV009, bubbleV008, bubbleV007);
            RibbonLayoutHelper.WirePulldownButton(renameItems, "Pulldown_SectionAutoRenamer", sectionRenamerV026, sectionRenamerV025);
            RibbonLayoutHelper.WirePulldownButton(renameItems, "Pulldown_ViewAutoRenamer", autoRenamerV007, autoRenamerV006, autoRenamerV005);
        }
    }
}
