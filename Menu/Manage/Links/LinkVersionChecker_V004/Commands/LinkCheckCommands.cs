using System;
using System.Reflection;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Services;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.ViewModels;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.UI.Views;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Commands
{
    /// <summary>Pick a host .rvt and check its links without opening it. Works with no project open.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkCheckPreOpen : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            LinkCheckLauncher.Show(data.Application, ScanMode.PreOpen, ref message);
    }

    /// <summary>Check the links of the open project.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkCheckInModel : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            Document doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("Link Version Checker", "Open a project first, or use Pre-Open / Folder Audit.");
                return Result.Cancelled;
            }
            return LinkCheckLauncher.Show(data.Application, ScanMode.InModel, ref message);
        }
    }

    /// <summary>Audit every host model in a project folder. Works with no project open.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkCheckFolder : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            LinkCheckLauncher.Show(data.Application, ScanMode.Folder, ref message);
    }

    /// <summary>Keeps Pre-Open and Folder Audit buttons enabled with no project open.</summary>
    public class LinkCheckAlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selected) => true;
    }

    internal static class LinkCheckLauncher
    {
        public static Result Show(UIApplication uiapp, ScanMode mode, ref string message)
        {
            try
            {
                var vm = new LinkCheckerViewModel(uiapp.Application, uiapp.ActiveUIDocument?.Document, mode);
                var win = new LinkCheckerWindow(vm);
                new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
                // Modal: every handler (and every await continuation) stays in Revit's API context.
                win.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
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

            var preOpen = new PushButtonData("LVC2_PreOpen", "Links\nPre-Open", asm, ns + nameof(CmdLinkCheckPreOpen))
            {
                AvailabilityClassName = always,
                ToolTip = "Check linked file versions of a model BEFORE opening it (no model is opened)."
            };
            var inModel = new PushButtonData("LVC2_InModel", "Links\nIn-Model", asm, ns + nameof(CmdLinkCheckInModel))
            {
                ToolTip = "Check links, IFC caches, imports and worksets in the open model."
            };
            var folder = new PushButtonData("LVC2_Folder", "Links\nFolder Audit", asm, ns + nameof(CmdLinkCheckFolder))
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
