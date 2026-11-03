using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRooms.V013
{
    /// <summary>
    /// V005 changes (confirmed spec, carried forward):
    /// - First link + first instance auto-selected on load; rooms load immediately.
    /// - Settings JSON persistence (last link / floor type / roof type) restored on open.
    /// - Regenerate() calls removed from the run loop (Revit regenerates on commit).
    /// - Progress-count and roof inner-loop-count bugs fixed.
    /// - New Level column upgraded to a live substring-filter dropdown.
    /// Roof creation logic itself is untouched from V004 per confirmed spec.
    ///
    /// V006 changes:
    /// - LoadRoofTypes() now logs a Warning when the host model has zero RoofType
    ///   elements, matching the existing pattern for LoadHostLevels()/LoadLinkedDocuments().
    ///   Diagnoses an empty Roof type dropdown instead of leaving it unexplained.
    ///
    /// V007 changes (confirmed spec):
    /// - New Level auto-match switched from name-based to ELEVATION-based
    ///   (LevelMatchingService.FindMatch), adjusted for the link instance's placement
    ///   transform. Tolerance is user-adjustable (mm, default 1) with an "Exact match"
    ///   override; no host level is ever created — unmatched rooms stay unmapped.
    /// - Metrics card added above the grid: live Total/Selected/Mapped/Unmapped on the
    ///   left, last-run Created/Skipped/Failed on the right (populates after a run).
    /// - Fixed prohibited pack://application:,,,/ SharedStyles reference (crashes at
    ///   load in Revit — no Application object); switched to assembly-relative pack URI.
    ///
    /// V010 changes:
    /// - RoofCreationService rebuilt on FootprintRoofFromLines's proven validation
    ///   pattern (compound-structure null check, full parameter/curve logging),
    ///   fixing roof-creation failures present through V008.
    /// - Namespace/version bump and refactor pass: stale version-numbered inline
    ///   comments normalized to plain descriptions; no functional/UI changes.
    /// - TODO (tracked, not yet implemented): confirm this command runs regardless
    ///   of active view type (Plan/3D/Section) — no restriction currently found in
    ///   this Command.cs, but flagged for review before next functional release.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteInternal(commandData, ref message, elements);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                Logger.Error("Command", ex);
                message = ex.Message;
                TaskDialog.Show("Floors And Roof From Linked Rooms", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            var handler = new RunCreateElementsExternalEventHandler();
            var externalEvent = ExternalEvent.Create(handler);

            var viewModel = new MainViewModel(doc, handler, externalEvent);
            handler.ViewModel = viewModel;

            var window = new FloorsFromLinkedRoomsWindow { DataContext = viewModel };
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();

            return Result.Succeeded;
        }
    }
}
