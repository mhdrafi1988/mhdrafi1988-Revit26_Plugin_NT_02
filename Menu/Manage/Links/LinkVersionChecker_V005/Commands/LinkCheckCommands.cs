using System;
using System.Reflection;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkVersionChecker_V005.Services;
using Revit26_Plugin_Revised.LinkVersionChecker_V005.UI.ViewModels;
using Revit26_Plugin_Revised.LinkVersionChecker_V005.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V005.Commands
{
    /// <summary>Pick a host .rvt and check its links without opening it. Works with no project open.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkCheckPreOpen : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), data, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => LinkCheckLauncher.Show(d.Application, ScanMode.PreOpen));
    }

    /// <summary>Check the links of the open project.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkCheckInModel : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), data, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData data, ref string message, ElementSet elements)
        {
            Document doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.LinkVersionCheckerV005.Title, "Open a project first, or use Pre-Open / Folder Audit.");
                return Result.Cancelled;
            }
            return LinkCheckLauncher.Show(data.Application, ScanMode.InModel);
        }
    }

    /// <summary>Audit every host model in a project folder. Works with no project open.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkCheckFolder : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), data, ref message, elements,
                (ExternalCommandData d, ref string m, ElementSet e) => LinkCheckLauncher.Show(d.Application, ScanMode.Folder));
    }

    /// <summary>Keeps Pre-Open and Folder Audit buttons enabled with no project open.</summary>
    public class LinkCheckAlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selected) => true;
    }

    internal static class LinkCheckLauncher
    {
        /// <summary>Opens the checker window modally. Errors propagate to <see cref="ToolGuard"/>.</summary>
        public static Result Show(UIApplication uiapp, ScanMode mode)
        {
            var vm = new LinkCheckerViewModel(uiapp.Application, uiapp.ActiveUIDocument?.Document, mode);
            var win = new LinkCheckerWindow(vm);
            new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
            // Modal: every handler (and every await continuation) stays in Revit's API context.
            win.ShowDialog();
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// One-call setup for your IExternalApplication.OnStartup:
    /// <code>LinkCheckStartup.Register(application, panel);</code>
    /// Starts the load-time logger and adds the three buttons as a stacked group.
    /// </summary>
    public static class LinkCheckStartup
    {
        public static void Register(UIControlledApplication app, RibbonPanel panel)
        {
            LinkLoadTimeLogger.Register(app.ControlledApplication);

            string asm = Assembly.GetExecutingAssembly().Location;
            string ns = typeof(CmdLinkCheckPreOpen).Namespace + ".";
            string always = ns + nameof(LinkCheckAlwaysAvailable);

            var preOpen = new PushButtonData("LVC3_PreOpen", "Links\nPre-Open", asm, ns + nameof(CmdLinkCheckPreOpen))
            {
                AvailabilityClassName = always,
                ToolTip = "Check linked file versions of a model BEFORE opening it (no model is opened). Desktop Connector files also show whether they have been downloaded yet."
            };
            var inModel = new PushButtonData("LVC3_InModel", "Links\nIn-Model", asm, ns + nameof(CmdLinkCheckInModel))
            {
                ToolTip = "Check links, IFC caches, imports and worksets in the open model — and ask ACC whether each cloud link is the current version."
            };
            var folder = new PushButtonData("LVC3_Folder", "Links\nFolder Audit", asm, ns + nameof(CmdLinkCheckFolder))
            {
                AvailabilityClassName = always,
                ToolTip = "Audit every host model in a project folder and export one report."
            };
            panel.AddStackedItems(preOpen, inModel, folder);
        }

        public static void Unregister(UIControlledApplication app) =>
            LinkLoadTimeLogger.Unregister(app.ControlledApplication);
    }
}
