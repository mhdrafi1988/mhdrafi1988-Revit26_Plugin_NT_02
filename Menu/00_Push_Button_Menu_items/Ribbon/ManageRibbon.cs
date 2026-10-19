using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    public static class ManageRibbon
    {
        // Every Manage button is a pulldown: highest version number first,
        // every older version below it in descending order. Each tool lists only
        // its latest two versions.
        private const string Rebuilt = " with the standard three-zone window layout.";

        private static PushButtonData Btn(string assemblyPath, string name, string text, string commandClass,
                                          string icon, string tool, string version, string detail = null)
        {
            return new PushButtonData(name, text, assemblyPath, commandClass)
            {
                Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons." + icon),
                ToolTip = RibbonLayoutHelper.VersionTip(tool, version, detail)
            };
        }

        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            // Two panels: general model-management tools, and a Worksets panel of its own.
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Manage");

            var overlapV003 = Btn(assemblyPath, "Btn_AnnotationOverlapDetection_V003", "Overlap Detection V003", "Revit26_Plugin_Revised.AnnotationOverlapDetection.V003.Command",
                "Manage.AnnotationOverlapDetection_16.png", "Annotation Overlap Detection", "V003", "V002" + Rebuilt);
            var overlapV004 = Btn(assemblyPath, "Btn_AnnotationOverlapDetection_V004", "Overlap Detection V004", "Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Command",
                "Manage.AnnotationOverlapDetection_16.png", "Annotation Overlap Detection", "V004", "V003 on the shared ToolWindowShell (capped lists).");

            var paraManagerV004 = Btn(assemblyPath, "Btn_ParaManager_V004", "ParaManager V004", "Revit26_Plugin_Revised.ParaManager.V004.ParaManagerCommand",
                "Manage.ParaManager_16.png", "ParaManager", "V004", "V003" + Rebuilt);
            var paraManagerV005 = Btn(assemblyPath, "Btn_ParaManager_V005", "ParaManager V005", "Revit26_Plugin_Revised.ParaManager.V005.ParaManagerCommand",
                "Manage.ParaManager_16.png", "ParaManager", "V005", "V004 on the shared ToolWindowShell (log in body, capped queue grid).");

            var wsebV003 = Btn(assemblyPath, "Btn_WorksetsElementsBrowser_WSEB003", "Worksets & Elements WSEB003", "Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB003.Commands.WorksetsElementsBrowserCommand",
                "Manage.WorksetsElementsBrowser_16.png", "Worksets & Elements Browser", "WSEB003", "WSEB002" + Rebuilt);
            var wsebV004 = Btn(assemblyPath, "Btn_WorksetsElementsBrowser_WSEB004", "Worksets & Elements WSEB004", "Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Commands.WorksetsElementsBrowserCommand",
                "Manage.WorksetsElementsBrowser_16.png", "Worksets & Elements Browser", "WSEB004", "WSEB003 on the shared ToolWindowShell (capped tree).");

            // Workset Manager is intentionally NOT on the ribbon — its commands and code
            // remain in the project (and in ToolCatalog) so it can be re-added later.

            var worksetRenamerV004 = Btn(assemblyPath, "Btn_WorksetRenamer_V004", "Workset Renamer V004", "Revit26_Plugin_Revised.WorksetRenamer.V004.Command",
                "SetupTools.WorksetRename_16.png", "Workset Renamer", "V004", "V003" + Rebuilt);
            var worksetRenamerV005 = Btn(assemblyPath, "Btn_WorksetRenamer_V005", "Workset Renamer V005", "Revit26_Plugin_Revised.WorksetRenamer.V005.Command",
                "SetupTools.WorksetRename_16.png", "Workset Renamer", "V005", "V004 on the shared ToolWindowShell (capped grid).");

            // Newest build first. "#" at the end of the label marks the version added last.
            var worksetCreatorV016 = Btn(assemblyPath, "Btn_WorksetCreator_V016", "Workset Creator V016 #",
                "Revit26_Plugin_Revised.WorksetCreator.V016.Commands.WorksetCreatorCommand",
                "SetupTools.WorksetManager_16.png", "Workset Creator", "V016",
                "V015 with its own tool name/version source (WorksetCreatorTool).");

            var worksetCreatorV015 = Btn(assemblyPath, "Btn_WorksetCreator_V015", "Workset Creator V015",
                "Revit26_Plugin_Revised.WorksetCreator.V015.Commands.WorksetCreatorCommand",
                "SetupTools.WorksetManager_16.png", "Workset Creator", "V015");

            var deleteWorksetV001 = Btn(assemblyPath, "Btn_DeleteWorkset_V001", "Delete Workset V001",
                "Revit26_Plugin_Revised.DeleteWorkset.V001.Commands.DeleteWorksetCommand",
                "SetupTools.WorksetManager_16.png", "Delete Workset", "V001",
                "Deletes one or more worksets and migrates their elements to a chosen target workset.");

            var deleteLineStylesV001 = Btn(assemblyPath, "Btn_DeleteLineStyles_V001", "Delete Line Styles V001",
                "Revit26_Plugin_Revised.DeleteLineStyles.V001.Commands.DeleteLineStylesCommand",
                "Manage.DeleteLineStyles_16.png", "Delete Line Styles", "V001");
            deleteLineStylesV001.ToolTip = Revit26_Plugin_Revised.Shared.Services.ToolCatalog.DeleteLineStyles.Tip(
                "Deletes unused custom line styles, or all custom line styles (their lines move to a replacement style).");

            var bulkRenameV001 = Btn(assemblyPath, "Btn_BulkRename_V001", "Bulk Rename V001",
                "Revit26_Plugin_Revised.BulkRename.V001.Commands.BulkRenameCommand",
                "Manage.BulkRename_16.png", "Bulk Rename", "V001");
            bulkRenameV001.ToolTip = Revit26_Plugin_Revised.Shared.Services.ToolCatalog.BulkRename.Tip(
                "Renames many line styles, line patterns, arrowheads or fill patterns at once: find/replace, prefix/suffix, case change and numbering.");

            var worksetRenamerFx04 = Btn(assemblyPath, "Btn_WorksetRenamer_FX04", "Workset Renamer (Excel) FX04", "Revit26_Plugin_Revised.WorksetRenamer.FX04.Command",
                "SetupTools.WorksetRename_16.png", "Workset Renamer (From Excel)", "FX04", "FX03" + Rebuilt);
            var worksetRenamerFx05 = Btn(assemblyPath, "Btn_WorksetRenamer_FX05", "Workset Renamer (Excel) FX05", "Revit26_Plugin_Revised.WorksetRenamer.FX05.Command",
                "SetupTools.WorksetRename_16.png", "Workset Renamer (Excel)", "FX05", "FX04 on the shared ToolWindowShell (capped group grids, shared group colours).");

            // Schedule Export/Import — latest two versions: V007 (ToolWindowShell) first,
            // then V006 (its three-zone-layout predecessor).
            var scheduleExportImportV006 = Btn(assemblyPath, "Btn_ScheduleExportImport_V006", "Schedule Export/Import V006", "Revit26_Plugin_Revised.ScheduleExportImport.V006.Commands.ScheduleExportImportCommand",
                "Manage.ScheduleExportImport_16.png", "Schedule Export / Import", "V006", "V005" + Rebuilt);
            var scheduleExportImportV007 = Btn(assemblyPath, "Btn_ScheduleExportImport_V007", "Schedule Export/Import V007", "Revit26_Plugin_Revised.ScheduleExportImport.V007.Commands.ScheduleExportImportCommand",
                "Manage.ScheduleExportImport_16.png", "Schedule Export / Import", "V007", "V006 on the shared ToolWindowShell (log in body, capped grids).");
            // Link Version Checker — one tool, three scan modes per version. Pre-Open and Folder
            // Audit stay enabled with no project open. The load-time logger the checker reads is
            // registered once, from V004: both versions share
            // %AppData%\Revit26_Plugin\LinkVersionChecker\loadtimes.json, so registering both
            // would double-handle Revit's link-load events and write the same file twice.
            Revit26_Plugin_Revised.LinkVersionChecker_V004.Services.LinkLoadTimeLogger.Register(app.ControlledApplication);
            const string lvcNs = "Revit26_Plugin_Revised.LinkVersionChecker_V003.Commands.";
            var linkPreOpenV003 = Btn(assemblyPath, "Btn_LinkVersionChecker_PreOpen_V003", "Link Check Pre-Open V003", lvcNs + "CmdLinkCheckPreOpen",
                "Manage.ParaManager_16.png", "Link Version Checker - Pre-Open", "V003", "Check a model's linked file versions BEFORE opening it (no model is opened).");
            linkPreOpenV003.AvailabilityClassName = lvcNs + "LinkCheckAlwaysAvailable";
            var linkInModelV003 = Btn(assemblyPath, "Btn_LinkVersionChecker_InModel_V003", "Link Check In-Model V003", lvcNs + "CmdLinkCheckInModel",
                "Manage.ParaManager_16.png", "Link Version Checker - In-Model", "V003", "Check links, IFC caches, imports and worksets in the open model.");
            var linkFolderV003 = Btn(assemblyPath, "Btn_LinkVersionChecker_Folder_V003", "Link Check Folder Audit V003", lvcNs + "CmdLinkCheckFolder",
                "Manage.ParaManager_16.png", "Link Version Checker - Folder Audit", "V003", "Audit every host model in a project folder and export one report.");
            linkFolderV003.AvailabilityClassName = lvcNs + "LinkCheckAlwaysAvailable";

            // Link Version Checker V004 — same three scan modes, now seeing ACC / cloud links
            // (V003 silently dropped them) plus a Source column and filter.
            const string lvc4Ns = "Revit26_Plugin_Revised.LinkVersionChecker_V004.Commands.";
            var linkPreOpenV004 = Btn(assemblyPath, "Btn_LinkVersionChecker_PreOpen_V004", "Link Check Pre-Open V004", lvc4Ns + "CmdLinkCheckPreOpen",
                "Manage.ParaManager_16.png", "Link Version Checker - Pre-Open", "V004", "Check a model's linked file versions BEFORE opening it (no model is opened). Sees ACC / cloud links.");
            linkPreOpenV004.AvailabilityClassName = lvc4Ns + "LinkCheckAlwaysAvailable";
            var linkInModelV004 = Btn(assemblyPath, "Btn_LinkVersionChecker_InModel_V004", "Link Check In-Model V004", lvc4Ns + "CmdLinkCheckInModel",
                "Manage.ParaManager_16.png", "Link Version Checker - In-Model", "V004", "Check links, IFC caches, imports and worksets in the open model, ACC / cloud links included.");
            var linkFolderV004 = Btn(assemblyPath, "Btn_LinkVersionChecker_Folder_V004", "Link Check Folder Audit V004", lvc4Ns + "CmdLinkCheckFolder",
                "Manage.ParaManager_16.png", "Link Version Checker - Folder Audit", "V004", "Audit every host model in a project folder and export one report.");
            linkFolderV004.AvailabilityClassName = lvc4Ns + "LinkCheckAlwaysAvailable";

            // Link Version Checker V005 — same three scan modes plus an ACC Docs column (green radio
            // dot: is the ACC copy current / downloaded?) and an on-demand Check Cloud Status button.
            // "#" at the end of a label marks the version added last.
            const string lvc5Ns = "Revit26_Plugin_Revised.LinkVersionChecker_V005.Commands.";
            var linkPreOpenV005 = Btn(assemblyPath, "Btn_LinkVersionChecker_PreOpen_V005", "Link Check Pre-Open V005 #", lvc5Ns + "CmdLinkCheckPreOpen",
                "Manage.ParaManager_16.png", "Link Version Checker - Pre-Open", "V005", "Check a model's linked file versions BEFORE opening it (no model is opened). Shows whether Desktop Connector files are downloaded.");
            linkPreOpenV005.AvailabilityClassName = lvc5Ns + "LinkCheckAlwaysAvailable";
            var linkInModelV005 = Btn(assemblyPath, "Btn_LinkVersionChecker_InModel_V005", "Link Check In-Model V005 #", lvc5Ns + "CmdLinkCheckInModel",
                "Manage.ParaManager_16.png", "Link Version Checker - In-Model", "V005", "Check links in the open model, with an ACC Docs status column: green dot when the ACC copy is current.");
            var linkFolderV005 = Btn(assemblyPath, "Btn_LinkVersionChecker_Folder_V005", "Link Check Folder Audit V005 #", lvc5Ns + "CmdLinkCheckFolder",
                "Manage.ParaManager_16.png", "Link Version Checker - Folder Audit", "V005", "Audit every host model in a project folder and export one report.");
            linkFolderV005.AvailabilityClassName = lvc5Ns + "LinkCheckAlwaysAvailable";

            // ── Manage panel ─────────────────────────────────────────────────
            var manageItems = RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_AnnotationOverlapDetection", "Overlaps", overlapV004),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_ParaManager", "ParaManager", paraManagerV005),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DeleteLineStyles", "Del. Line Styles", deleteLineStylesV001),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_BulkRename", "Bulk Rename", bulkRenameV001),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_ScheduleExportImport", "Schedule I/O", scheduleExportImportV007),
            });
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_AnnotationOverlapDetection", overlapV004, overlapV003);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_ParaManager", paraManagerV005, paraManagerV004);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_DeleteLineStyles", deleteLineStylesV001);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_BulkRename", bulkRenameV001);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_ScheduleExportImport", scheduleExportImportV007, scheduleExportImportV006);

            // ── Worksets panel ───────────────────────────────────────────────
            // Every workset tool lives here, in its own panel, instead of being
            // mixed into Manage.
            RibbonPanel worksetPanel = app.CreateRibbonPanel(tabName, "Worksets");

            var worksetItems = RibbonLayoutHelper.AddStackedButtons(worksetPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetsElementsBrowser", "WS Browser", wsebV004),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetCreator", "WS Creator", worksetCreatorV016),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetRenamer", "WS Renamer", worksetRenamerV005),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetRenamerFx", "WS Renamer XL", worksetRenamerFx05),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DeleteWorkset", "Delete Workset", deleteWorksetV001),
            });
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetsElementsBrowser", wsebV004, wsebV003);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetCreator", worksetCreatorV016, worksetCreatorV015);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetRenamer", worksetRenamerV005, worksetRenamerV004);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetRenamerFx", worksetRenamerFx05, worksetRenamerFx04);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_DeleteWorkset", deleteWorksetV001);

            // ── Links panel ──────────────────────────────────────────────────
            // Link tools. Link Check lists V005's three scan modes first, then V004's, then V003's.
            RibbonPanel linksPanel = app.CreateRibbonPanel(tabName, "Links");

            var linkItems = RibbonLayoutHelper.AddStackedButtons(linksPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_LinkVersionChecker", "Link Check", linkInModelV005),
            });
            RibbonLayoutHelper.WirePulldownButton(linkItems, "Pulldown_LinkVersionChecker",
                linkInModelV005, linkPreOpenV005, linkFolderV005,
                linkInModelV004, linkPreOpenV004, linkFolderV004,
                linkInModelV003, linkPreOpenV003, linkFolderV003);
        }
    }
}
