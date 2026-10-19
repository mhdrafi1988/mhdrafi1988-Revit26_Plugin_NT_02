using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Name and version of one ribbon tool.
    /// </summary>
    public sealed class ToolInfo
    {
        /// <summary>Display name, used in tooltips, window titles and error dialogs.</summary>
        public string Name { get; }

        /// <summary>Semantic version (MAJOR.MINOR.PATCH).</summary>
        public string Version { get; }

        /// <summary>Legacy build tag the tool's folder and namespace carry (e.g. V004).</summary>
        public string BuildTag { get; }

        /// <summary>Root namespace of the tool's code; every type under it belongs to this tool.</summary>
        public string NamespacePrefix { get; }

        internal ToolInfo(string name, string version, string buildTag, string namespacePrefix)
        {
            Name = name;
            Version = version;
            BuildTag = buildTag;
            NamespacePrefix = namespacePrefix;
        }

        /// <summary>"Name — vX.Y.Z", used as the tooltip title line and window-title suffix.</summary>
        public string Title => $"{Name} — v{Version}";

        /// <summary>
        /// Ribbon tooltip: the "Name — vX.Y.Z (build Vnnn)" title line, then an optional description.
        /// </summary>
        public string Tip(string detail = null)
        {
            string title = $"{Title} (build {BuildTag})";
            return string.IsNullOrWhiteSpace(detail) ? title : title + "\n" + detail;
        }
    }

    /// <summary>
    /// Single source of truth for every tool's name and semantic version.
    /// Bump a tool's version here (and in CHANGELOG.md) whenever its behaviour changes;
    /// the ribbon tooltip, window title and error dialogs all read from this list.
    /// </summary>
    public static class ToolCatalog
    {
        private static readonly List<ToolInfo> All = new();

        public static readonly ToolInfo LinkedDetailLineGenerator = Add("Detail Lines From Links", "10.1.1", "VA010", "Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010");
        public static readonly ToolInfo DetailLineClosedLoop = Add("Detail Line Closed Loop", "3.0.1", "V003", "Revit26_Plugin_Revised.DetailLineClosedLoop.V003");
        public static readonly ToolInfo DtlLineDim = Add("Auto Dim Detail Line", "10.1.1", "V010", "Revit26_Plugin_Revised.DtlLineDim.V010");
        public static readonly ToolInfo FloorsAndRoofFromLinkedRooms = Add("Floors And Roof From Linked Rooms", "13.1.1", "V013", "Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V013");
        public static readonly ToolInfo FloorsAndRoofFromLinkedRoomsViaPlanView = Add("Floors And Roof From Linked Rooms (Via Plan View)", "6.1.1", "V006", "Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRoomsViaPlanView.V006");
        public static readonly ToolInfo ParaManager = Add("ParaManager", "5.1.1", "V005", "Revit26_Plugin_Revised.ParaManager.V005");
        public static readonly ToolInfo ScheduleExportImport = Add("Schedule Export / Import", "7.1.1", "V007", "Revit26_Plugin_Revised.ScheduleExportImport.V007");
        public static readonly ToolInfo AnnotationOverlapDetection = Add("Annotation Overlap Detection", "4.1.1", "V004", "Revit26_Plugin_Revised.AnnotationOverlapDetection.V004");
        public static readonly ToolInfo WorksetsElementsBrowser = Add("Worksets & Elements Browser", "4.2.1", "WSEB004", "Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004");
        public static readonly ToolInfo WorksetManager = Add("Workset Manager", "14.1.1", "V014", "Revit26_Plugin_Revised.WorksetManager.V014");
        public static readonly ToolInfo WorksetRenamer = Add("Workset Renamer", "5.1.1", "V005", "Revit26_Plugin_Revised.WorksetRenamer.V005");
        public static readonly ToolInfo WorksetRenamerExcel = Add("Workset Renamer (From Excel)", "5.1.1", "FX05", "Revit26_Plugin_Revised.WorksetRenamer.FX05");
        public static readonly ToolInfo AutoSlopeByPoint = Add("Auto Slope By Point", "30.0.1", "V030", "Revit26_Plugin_Revised.AutoSlopeByPoint.V030");
        public static readonly ToolInfo AutoSlopeByPointV032 = Add("Auto Slope By Point", "32.1.0", "V032", "Revit26_Plugin_Revised.AutoSlopeByPoint.V032");
        public static readonly ToolInfo AutoSlopeByPointRidge = Add("Auto Slope By Point (Ridge)", "3.1.1", "V003", "Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003");
        public static readonly ToolInfo AutoSlopeByPointMultiCopies = Add("Auto Slope By Point (Multi Copies)", "30.1.1", "MultiCopies30", "Revit26_Plugin_Revised.AutoSlopeByPoint.MultiCopies30");
        public static readonly ToolInfo AutoSlopeByDrain = Add("Auto Slope By Drain", "7.2.0", "V007", "Revit26_Plugin_Revised.AutoSlopeByDrain.V007");
        public static readonly ToolInfo MultiRoofSlopeByDrain = Add("Auto Slope By Drain (Multi-Roof)", "12.0.1", "V012", "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V012");
        public static readonly ToolInfo MultiRoofSlopeByDrainV013 = Add("Auto Slope By Drain (Multi-Roof)", "13.1.1", "V013", "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013");
        public static readonly ToolInfo InnerLoopDivider = Add("Divide Inner Loops", "12.1.1", "V012", "Revit26_Plugin_Revised.InnerLoopDivider.V012");
        public static readonly ToolInfo OuterCurveDivider = Add("Outer Curve Divider", "6.0.1", "V006", "Revit26_Plugin_Revised.OuterCurveDivider.V006");
        public static readonly ToolInfo RoofDetailLineIntersect = Add("Roof Detail Line Intersect", "14.1.1", "V014", "Revit26_Plugin_Revised.RoofDetailLineIntersect.V014");
        public static readonly ToolInfo InnerLoopsAndPerpendicular = Add("Inner Loops And Perpendicular", "7.1.1", "V007", "Revit26_Plugin_Revised.InnerLoopsAndPerpendicular.V007");
        public static readonly ToolInfo RoofEdgeVertexReducer = Add("Roof Edge Vertex Reducer", "9.0.1", "V009", "Revit26_Plugin_Revised.RoofEdgeVertexReducer.V009");
        public static readonly ToolInfo MultiplePoints = Add("Multiple Points (1/2–1/4)", "3.0.1", "V003", "Revit26_Plugin_Revised.MultiplePoints.V003");
        public static readonly ToolInfo RidgeByOpenings = Add("Ridge By Openings", "70.1.0", "V070", "Revit26_Plugin_Revised.RoofRidgeLines.V070");
        public static readonly ToolInfo RidgeByPoints = Add("Ridge By Points", "59.1.0", "V059", "Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V059");
        public static readonly ToolInfo CreaserAdv = Add("Creaser Adv", "12.0.1", "V012", "Revit26_Plugin_Revised.CreaserAdv.V012");
        public static readonly ToolInfo RoofTag = Add("Roof Tag", "18.1.1", "V018", "Revit26_Plugin_Revised.RoofTag.V018");
        // Source not in the repository; removed from the ribbon in plugin 3.10.0. Kept so logs stay readable.
        public static readonly ToolInfo RoofFromDetailLines = Add("Roof From Detail Lines", "9.0.0", "V009", "Revit26_Plugin_Revised.RoofFromDetailLines.V009");
        public static readonly ToolInfo CombinedRoofTools = Add("Combined Roof Tools", "3.1.1", "V003", "Revit26_Plugin_Revised.CombinedRoofTools.V003");
        public static readonly ToolInfo RoofTypeManager = Add("Roof Type Manager", "3.1.1", "V003", "Revit26_Plugin_Revised.RoofTypeCreator.V003");
        public static readonly ToolInfo RoofPointElevationSync = Add("Roof Point Elevation Sync", "5.0.1", "V005", "Revit26_Plugin_Revised.RoofPointElevationSync.V005");
        public static readonly ToolInfo RoofPointComparison = Add("Roof Point Comparison", "3.0.1", "V003", "Revit26_Plugin_Revised.RoofPointComparison.V003");
        public static readonly ToolInfo TaperedEpsPattern2D = Add("Tapered EPS 2D Pattern", "6.1.0", "V006", "Revit26_Plugin_Revised.TaperedEpsPattern2D.V006");
        public static readonly ToolInfo BatchDwgFamilyLinker = Add("Batch Link DWG Family", "2.2.0", "V002", "BatchDwgFamilyLinker.V002");
        public static readonly ToolInfo DwgToLines = Add("DWG To Lines", "7.0.1", "V007", "Revit26_Plugin_Revised.DwgToLines.V007");
        public static readonly ToolInfo DwgToDetailLines = Add("DWG To Detail Lines", "11.0.0", "V011", "Revit26_Plugin_Revised.DwgToDetailLines.V011");
        public static readonly ToolInfo DwgToDlp = Add("DWG To DL(P)", "14.0.1", "V014", "Revit26_Plugin_Revised.DwgToDetailLines.V014");
        public static readonly ToolInfo DwgManager = Add("DWG Manager", "3.1.1", "V003", "Revit26_Plugin_Revised.DwgManager.V003");
        public static readonly ToolInfo DwgStatistics = Add("DWG Statistics", "5.1.1", "V005", "Revit26_Plugin_Revised.DwgStatistics.V005");
        public static readonly ToolInfo ExportDwgToFolder = Add("Export DWG to Folder", "4.1.1", "V004", "Revit26_Plugin_Revised.ExportDwgToFolder.V004");
        public static readonly ToolInfo PlanFromScopeBox = Add("Plan From Scope Box", "6.1.1", "V006", "Revit26_Plugin_Revised.PlanFromScopeBox.V006");
        public static readonly ToolInfo SmartViewToSheetPlacer = Add("Smart View To Sheet Placer", "224.1.1", "V224", "Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224");
        public static readonly ToolInfo AutomatedSectionPlacer = Add("Automated Section Placer", "3.1.1", "V003", "Revit26_Plugin_Revised.AutomatedSectionPlacer.V003");
        public static readonly ToolInfo SheetAutoRearrange = Add("Sheet Auto Rearrange", "28.1.1", "V028", "Revit26_Plugin_Revised.SheetAutoRearrange.V028");
        public static readonly ToolInfo SheetViewArrange = Add("Sheet View Arrange", "1.2.1", "V001", "Revit26_Plugin_Revised.SheetViewArrange.V001");
        public static readonly ToolInfo RoofEdgeAroundSections = Add("Roof Edge Around Sections", "7.1.1", "V007", "Revit26_Plugin_Revised.RoofEdgeAroundSections.V007");
        public static readonly ToolInfo RoofEdgeElementSections = Add("Roof Edge Element Sections", "4.1.1", "V004", "Revit26_Plugin_Revised.RoofEdgeElementSections.V004");
        public static readonly ToolInfo RoofNameBuilder = Add("Roof Name Builder", "2.1.1", "V002", "Revit26_Plugin_Revised.RoofNameBuilder.V002");
        public static readonly ToolInfo IslandRoofSplitter = Add("Island Roof Splitter", "0.2.1", "V001 (WIP)", "Revit26_Plugin_Revised.IslandRoofSplitter.V001");
        public static readonly ToolInfo IslandRoofSplitterV002 = Add("Island Roof Splitter", "2.0.0", "V002", "Revit26_Plugin_Revised.IslandRoofSplitter.V002");
        public static readonly ToolInfo LinkVersionChecker = Add("Link Version Checker", "3.0.1", "V003", "Revit26_Plugin_Revised.LinkVersionChecker_V003");
        public static readonly ToolInfo LinkVersionCheckerV004 = Add("Link Version Checker", "4.2.0", "V004", "Revit26_Plugin_Revised.LinkVersionChecker_V004");
        public static readonly ToolInfo RoofViewFocus = Add("Roof View Focus", "4.1.1", "V004", "Revit26_Plugin_Revised.RoofViewFocus.V004");
        public static readonly ToolInfo CreateSections = Add("Create Sections From Detail Lines", "13.1.1", "V013", "Revit26_Plugin_Revised.CreateSections.V013");
        public static readonly ToolInfo APUS = Add("Auto Place Sections", "324.1.1", "V324", "Revit26_Plugin_Revised.APUS.V324");
        public static readonly ToolInfo CalloutCOP = Add("Callout To Section View Placement", "21.1.1", "V021", "Revit26_Plugin_Revised.CalloutCOP.V021");
        public static readonly ToolInfo RefSectionHeadPlacer = Add("Reference Section Head Placer", "15.1.1", "V015", "Revit26_Plugin_Revised.RefSectionHeadPlacer.V015");
        public static readonly ToolInfo SectionViewAutoTagger = Add("Section View Auto Tagger", "6.1.1", "V006", "Revit26_Plugin_Revised.SectionViewAutoTagger.V006");
        public static readonly ToolInfo ViewAutoRenamer = Add("View Auto Renamer", "6.1.1", "V006", "Revit26_Plugin_Revised.ViewAutoRenamer.V006");
        public static readonly ToolInfo BubbleAutoRenumber = Add("Bubble Auto Renumber", "8.0.1", "V008", "Revit26_Plugin_Revised.BubbleAutoRenumber.V008");
        public static readonly ToolInfo BubbleAutoRenumberV009 = Add("Bubble Auto Renumber", "9.1.1", "V009", "Revit26_Plugin_Revised.BubbleAutoRenumber.V009");
        public static readonly ToolInfo SectionAutoRenamer = Add("Section Auto Renamer", "26.1.1", "V026", "Revit26_Plugin_Revised.SectionAutoRenamer.V026");
        public static readonly ToolInfo DeleteWorkset = Add("Delete Workset", "1.2.0", "V001", "Revit26_Plugin_Revised.DeleteWorkset.V001");
        public static readonly ToolInfo DeleteLineStyles = Add("Delete Line Styles", "1.2.0", "V001", "Revit26_Plugin_Revised.DeleteLineStyles.V001");
        public static readonly ToolInfo BulkRename = Add("Bulk Rename", "1.1.1", "V001", "Revit26_Plugin_Revised.BulkRename.V001");
        public static readonly ToolInfo WorksetCreator = Add("Workset Creator", "16.1.1", "V016", "Revit26_Plugin_Revised.WorksetCreator.V016");

        private static ToolInfo Add(string name, string version, string buildTag, string namespacePrefix)
        {
            var info = new ToolInfo(name, version, buildTag, namespacePrefix);
            All.Add(info);
            return info;
        }

        /// <summary>
        /// Finds the tool that owns <paramref name="type"/> by namespace, or null for shared/ribbon code.
        /// </summary>
        public static ToolInfo Find(Type type) => FindByNamespace(type?.Namespace);

        /// <summary>
        /// Finds the tool that owns the type named <paramref name="typeFullName"/>
        /// (e.g. a ribbon button's command class name), or null when no tool matches.
        /// </summary>
        public static ToolInfo Find(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return null;
            int dot = typeFullName.LastIndexOf('.');
            return dot > 0 ? FindByNamespace(typeFullName.Substring(0, dot)) : null;
        }

        private static ToolInfo FindByNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return null;
            return All
                .Where(t => ns == t.NamespacePrefix || ns.StartsWith(t.NamespacePrefix + ".", StringComparison.Ordinal))
                .OrderByDescending(t => t.NamespacePrefix.Length)
                .FirstOrDefault();
        }
    }
}
