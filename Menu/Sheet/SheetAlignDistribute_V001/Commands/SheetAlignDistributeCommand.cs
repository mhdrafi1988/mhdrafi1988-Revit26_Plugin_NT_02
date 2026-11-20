using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Infrastructure;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.ViewModels;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Commands
{
    /// <summary>
    /// Opens Sheet Align &amp; Distribute for the sheet the user has open: lines its views up on the
    /// rows and columns they are already in, moving each one as little as possible. Nothing is
    /// re-ordered and no other sheet is read.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetAlignDistributeCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc?.Document == null)
            {
                TaskDialog.Show(ToolCatalog.SheetAlignDistribute.Title, "Open a project first.");
                return Result.Cancelled;
            }

            // The tool only ever works on the sheet in front of the user, so there is nothing to
            // choose and nothing on another sheet can be moved by accident.
            if (uiDoc.ActiveView is not ViewSheet sheet || sheet.IsPlaceholder)
            {
                TaskDialog.Show(ToolCatalog.SheetAlignDistribute.Title,
                    "Open the sheet you want to align, then run the tool again.");
                return Result.Cancelled;
            }

            // One window at a time: a second click brings the open one to the front, so two windows
            // can never plan against the same sheet at once.
            if (TryActivateOpenWindow(sheet.Id))
                return Result.Succeeded;

            // Read the sheet the way the saved settings measure it, so the first preview and the
            // check Apply makes against it use the same footprints.
            var saved = SettingsService<AlignDistributeSettings>.Load("SheetAlignDistribute");
            var session = new RevitSheetAlignSession(uiDoc.Document, sheet, saved?.Footprint ?? FootprintMode.BoxAndTitle);
            SheetAlignDistributeWindow window;
            try
            {
                window = new SheetAlignDistributeWindow(new ViewModel_SheetAlignDistribute(session), sheet.Id);
            }
            catch
            {
                session.Dispose();
                throw;
            }

            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show(); // modeless — Refresh / Apply go through an ExternalEvent
            return Result.Succeeded;
        }

        /// <summary>Brings an already open window for this sheet to the front; true when one was found.</summary>
        private static bool TryActivateOpenWindow(ElementId sheetId)
        {
            var open = Application.Current?.Windows
                .OfType<SheetAlignDistributeWindow>()
                .FirstOrDefault(w => w.SheetId == sheetId);

            if (open == null) return false;

            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return true;
        }
    }
}
