using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Resources.Icons;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.Menu.Ribbon
{
    /// <summary>Builds the Manage and Worksets panels of the SloperPro Documents tab.</summary>
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

        /// <summary>Creates the Manage panel, then the Worksets panel, on <paramref name="tabName"/>.</summary>
        public static void Build(UIControlledApplication app, string tabName, string assemblyPath)
        {
            // Two panels: general model-management tools (Link Check included), and a Worksets panel of its own.
            RibbonPanel panel = app.CreateRibbonPanel(tabName, "Manage");

            var overlapV004 = Btn(assemblyPath, "Btn_AnnotationOverlapDetection_V004", "Overlap Detection V004", "Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Command",
                "Manage.AnnotationOverlapDetection_16.png", "Annotation Overlap Detection", "V004", "V003 on the shared ToolWindowShell (capped lists).");

            var paraManagerV005 = Btn(assemblyPath, "Btn_ParaManager_V005", "ParaManager V005", "Revit26_Plugin_Revised.ParaManager.V005.ParaManagerCommand",
                "Manage.ParaManager_16.png", "ParaManager", "V005", "V004 on the shared ToolWindowShell (log in body, capped queue grid).");

            var sharedParamStudioV001 = Btn(assemblyPath, "Btn_SharedParamStudio_V001", "Shared Params", "Revit26_Plugin_Revised.SharedParamStudio.V001.Commands.SharedParamStudioCommand",
                "Manage.ParaManager_16.png", "Shared Parameter Studio", "V001", "Create and assign shared parameters on the go, merge two shared parameter files, check a file for duplicates, retrieve the project's shared parameters.");

            var wsebV003 = Btn(assemblyPath, "Btn_WorksetsElementsBrowser_WSEB003", "Worksets & Elements WSEB003", "Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB003.Commands.WorksetsElementsBrowserCommand",
                "Manage.WorksetsElementsBrowser_16.png", "Worksets & Elements Browser", "WSEB003", "WSEB002" + Rebuilt);
            var wsebV004 = Btn(assemblyPath, "Btn_WorksetsElementsBrowser_WSEB004", "Worksets & Elements WSEB004", "Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Commands.WorksetsElementsBrowserCommand",
                "Manage.WorksetsElementsBrowser_16.png", "Worksets & Elements Browser", "WSEB004", "WSEB003 on the shared ToolWindowShell (capped tree).");

            // Workset Manager — back on the ribbon in plugin 5.8.0 (Worksets panel).
            var worksetManagerV013 = Btn(assemblyPath, "Btn_WorksetManager_V013", "Workset Manager V013",
                "Revit26_Plugin_Revised.WorksetManager.V013.Commands.WorksetManagerCommand",
                "SetupTools.WorksetManager_16.png", "Workset Manager", "V013");
            var worksetManagerV014 = Btn(assemblyPath, "Btn_WorksetManager_V014", "Workset Manager V014",
                "Revit26_Plugin_Revised.WorksetManager.V014.Commands.WorksetManagerCommand",
                "SetupTools.WorksetManager_16.png", "Workset Manager", "V014");

            var worksetRenamerV005 = Btn(assemblyPath, "Btn_WorksetRenamer_V005", "Workset Renamer V005", "Revit26_Plugin_Revised.WorksetRenamer.V005.Command",
                "SetupTools.WorksetRename_16.png", "Workset Renamer", "V005", "V004 on the shared ToolWindowShell (capped grid).");

            // Newest build first.
            var worksetCreatorV017 = Btn(assemblyPath, "Btn_WorksetCreator_V017", "Workset Creator V017",
                "Revit26_Plugin_Revised.WorksetCreator.V017.Commands.WorksetCreatorCommand",
                "SetupTools.WorksetManager_16.png", "Workset Creator", "V017");

            var worksetCreatorV016 = Btn(assemblyPath, "Btn_WorksetCreator_V016", "Workset Creator V016",
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

            var trackerIdV001 = Btn(assemblyPath, "Btn_TrackerId_V001", "Tracker ID V001",
                "Revit26_Plugin_Revised.TrackerId.V001.Commands.TrackerIdCommand",
                "Manage.TrackerId_16.png", "Tracker ID", "V001");
            trackerIdV001.LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.Manage.TrackerId_32.png");
            trackerIdV001.ToolTip = Revit26_Plugin_Revised.Shared.Services.ToolCatalog.TrackerId.Tip(
                "Gives each element of a chosen category and types one permanent Tracker_ID " +
                "(Zone-Level-Type-Grid + 3 custom segments), detects copied and moved elements, " +
                "and exports a CSV for Power BI progress and material tracking.");

            // V002 coexists with V001 (same Tracker_ID parameters, its own settings).
            var trackerIdV002 = Btn(assemblyPath, "Btn_TrackerId_V002", "Tracker ID V002",
                "Revit26_Plugin_Revised.TrackerId.V002.Commands.TrackerIdCommand",
                "Manage.TrackerId_16.png", "Tracker ID", "V002");
            trackerIdV002.LargeImage = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.Manage.TrackerId_32.png");
            trackerIdV002.ToolTip = Revit26_Plugin_Revised.Shared.Services.ToolCatalog.TrackerIdV002.Tip(
                "Tracker IDs for several categories at once (whole model, selection or active view), " +
                "a separate naming page, status from SharePoint written into Revit and coloured views, " +
                "a model health check, and CSV or Excel export for Power BI.");

            // Removed in plugin 4.2.1 because their source is not in the repository, so the
            // buttons could only raise an error: Overlap Detection V003, ParaManager V004,
            // Workset Renamer V004 and FX04.

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

            // Link Version Checker V005 — adds the ACC Sync column and Desktop Connector download
            // state. Its own LinkLoadTimeLogger is NOT registered: it writes the same
            // loadtimes.json as V004's, which is already registered above.
            const string lvc5Ns = "Revit26_Plugin_Revised.LinkVersionChecker_V005.Commands.";
            var linkPreOpenV005 = Btn(assemblyPath, "Btn_LinkVersionChecker_PreOpen_V005", "Link Check Pre-Open V005", lvc5Ns + "CmdLinkCheckPreOpen",
                "Manage.ParaManager_16.png", "Link Version Checker - Pre-Open", "V005", "Check a model's linked file versions BEFORE opening it. Shows whether Desktop Connector files are downloaded.");
            linkPreOpenV005.AvailabilityClassName = lvc5Ns + "LinkCheckAlwaysAvailable";
            var linkInModelV005 = Btn(assemblyPath, "Btn_LinkVersionChecker_InModel_V005", "Link Check In-Model V005", lvc5Ns + "CmdLinkCheckInModel",
                "Manage.ParaManager_16.png", "Link Version Checker - In-Model", "V005", "Check the open model's links, and ask ACC whether each cloud link is the current version.");
            var linkFolderV005 = Btn(assemblyPath, "Btn_LinkVersionChecker_Folder_V005", "Link Check Folder Audit V005", lvc5Ns + "CmdLinkCheckFolder",
                "Manage.ParaManager_16.png", "Link Version Checker - Folder Audit", "V005", "Audit every host model in a project folder and export one report.");
            linkFolderV005.AvailabilityClassName = lvc5Ns + "LinkCheckAlwaysAvailable";

            // ── Manage panel ─────────────────────────────────────────────────
            var manageItems = RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_AnnotationOverlapDetection", "Overlaps", overlapV004),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_ParaManager", "ParaManager", paraManagerV005),
                sharedParamStudioV001,
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DeleteLineStyles", "Del. Line Styles", deleteLineStylesV001),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_BulkRename", "Bulk Rename", bulkRenameV001),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_ScheduleExportImport", "Schedule I/O", scheduleExportImportV007),
                // Link Check moved here from its own one-button Links panel (plugin 4.2.1).
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_LinkVersionChecker", "Link Check", linkInModelV005),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_TrackerId", "Tracker ID", trackerIdV002),
            });
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_AnnotationOverlapDetection", overlapV004);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_ParaManager", paraManagerV005);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_DeleteLineStyles", deleteLineStylesV001);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_BulkRename", bulkRenameV001);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_TrackerId", trackerIdV002, trackerIdV001);
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_ScheduleExportImport", scheduleExportImportV007, scheduleExportImportV006);
            // Link Check lists V005's three scan modes first, then V004's, then V003's.
            RibbonLayoutHelper.WirePulldownButton(manageItems, "Pulldown_LinkVersionChecker",
                linkInModelV005, linkPreOpenV005, linkFolderV005,
                linkInModelV004, linkPreOpenV004, linkFolderV004,
                linkInModelV003, linkPreOpenV003, linkFolderV003);

            // ── Worksets panel ───────────────────────────────────────────────
            // Every workset tool lives here, in its own panel, instead of being
            // mixed into Manage.
            RibbonPanel worksetPanel = app.CreateRibbonPanel(tabName, "Worksets");

            var worksetItems = RibbonLayoutHelper.AddStackedButtons(worksetPanel, new List<RibbonItemData>
            {
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetsElementsBrowser", "WS Browser", wsebV004),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetCreator", "WS Creator", worksetCreatorV017),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetManager", "WS Manager", worksetManagerV014),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetRenamer", "WS Renamer", worksetRenamerV005),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_WorksetRenamerFx", "WS Renamer XL", worksetRenamerFx05),
                RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_DeleteWorkset", "Delete Workset", deleteWorksetV001),
            });
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetsElementsBrowser", wsebV004, wsebV003);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetCreator", worksetCreatorV017, worksetCreatorV016, worksetCreatorV015);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetManager", worksetManagerV014, worksetManagerV013);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetRenamer", worksetRenamerV005);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_WorksetRenamerFx", worksetRenamerFx05);
            RibbonLayoutHelper.WirePulldownButton(worksetItems, "Pulldown_DeleteWorkset", deleteWorksetV001);
        }
    }
}
