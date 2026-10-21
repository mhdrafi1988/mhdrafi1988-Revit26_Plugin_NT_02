using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DwgStatistics.V005.Core.Models;
using Revit26_Plugin_Revised.DwgStatistics.V005.UI.ViewModels;
using Revit26_Plugin_Revised.DwgStatistics.V005.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.DwgStatistics.V005.Commands
{
    // V005 (2026-10-13) — bumped from V004, per Anas's confirmed list:
    //   • Far from origin: farthest DWG geometry (bounding box) from the internal origin; flagged above a
    //     configurable limit (default 10 km). "Max km" column, distance per instance.
    //   • Updated on disk: link file newer than at the last scan → warning; clears after Reload Links.
    //   • Layers: per-view hidden / overridden layers (Layers tab, view columns), "Many layers" over a limit
    //     (default 100), Hide / Unhide ticked layers in ticked views.
    //   • Workset rule: instances not on a CAD workset ("CAD;Link" contains) → warning; Move to Workset.
    //   • Shared history: _DwgStatistics folder next to the project / central file (team-wide "since last
    //     scan" + scan log), fallback %AppData% for cloud/server/unwritable; reads V004 snapshots once.
    //   • Rules & history settings remembered; changing a rule re-evaluates the last scan without rescanning.
    // V004 hardening (2026-10-13, in place — fixes, not a behaviour change):
    //   • Every UI-thread command and dispatcher callback runs through Guarded(): an unexpected
    //     exception is logged + ToolGuard.Report'ed instead of reaching Revit's dispatcher.
    //   • Per-view FilteredElementCollectors are disposed (scan hot loop, IsDrawnIn).
    //   • Closing the document closes this window (DocumentClosing; unsubscribed on close).
    // V004 (2026-10-13) — bumped from V002 (V003 skipped), per Anas's confirmed list:
    //   • Deep scan runs in batches (DwgStatsScanner.Begin + ScanBatch, ~200 ms each): the window stays
    //     responsive, the status bar shows real %, and "Cancel Scan" stops it (previous results kept).
    //   • Smarter opening: max views 1–20, zoom to the DWG in each view, open the sheet instead,
    //     close other open views.
    //   • More health checks: Shows in 3D, Absolute path, Not on sheet; tooltips say how to fix;
    //     Excel gains "Issues & Fixes" and "Changes" sheets.
    //   • Views tab: Hide / Unhide in Ticked Views (scan now records "Hidden here" views) and
    //     Delete in Ticked Views (from DWG Manager; 'Current view only' instances only, optional purge).
    //   • Compare with last scan: per-project JSON snapshot in %AppData%\Revit26_Plugin_Revised\DwgStatistics;
    //     Changed tile/chip/column and a "Changes since last scan" card.
    //   • Remembers filter chip, detail tab and open/delete options between sessions.
    // V002 (2026-10-13) — bumped from V001, per Anas's request:
    //   • Views tab of the selected DWG now has a tick column: tick the views you need, then
    //     "Open Ticked Views" opens them all (up to MaxOpenViews at once, same idea as DWG Manager V003).
    //     Tick All / Clear in the tab; the per-row Open button stays for a quick single view.
    //   • View ticks are kept across Refresh and cleanup actions (by DWG type + view).
    // V001 (2026-10-13) — new tool, per Anas's confirmed plan:
    //   • One row per DWG file (CAD link or import type): instances (view-specific / model), views it is
    //     placed in, views it is visible in, sheets, pinned, layers, link status and issue badges.
    //   • Always deep-scans every graphical view to count where each DWG is actually drawn.
    //   • Cleanup actions on ticked files: Pin All, Reload Links, Purge Unused (each one undo step).
    //   • Export to .xlsx (Summary / Views / Instances / Scan Info) with no extra NuGet dependency.
    //   • Reuses DWG Manager V003's patterns: single window per session, queued external event,
    //     ToolWindowShell, shared LogEntry log.
    /// <summary>
    /// Opens the DWG Statistics window (single instance per Revit session). Running it again brings the
    /// open window forward, or reopens it for the active document if the user switched documents.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DwgStatisticsCommand : IExternalCommand
    {
        private static DwgStatisticsWindow _window;

        // V004 hardening: the Revit application whose DocumentClosing event is watched while a window is open.
        private static Autodesk.Revit.ApplicationServices.Application _watchedApp;

        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc?.Document == null)
            {
                TaskDialog.Show(DwgStatisticsTool.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (uidoc.Document.IsFamilyDocument)
            {
                TaskDialog.Show(DwgStatisticsTool.Title, "DWG Statistics works in a project, not in the Family Editor.");
                return Result.Cancelled;
            }

            if (_window != null)
            {
                if (_window.ViewModel.IsFor(uidoc.Document))
                {
                    if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    return Result.Succeeded;
                }

                // Opened for another document: replace it with a window for the active one.
                _window.Close();
            }

            // The first scan is queued from the window's ContentRendered, so the window (with its
            // "Scanning…" status) is on screen before the deep scan starts.
            var viewModel = new DwgStatisticsViewModel(uidoc.Document);
            var window = new DwgStatisticsWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                    StopWatchingDocuments();
                }
            };

            _window = window;
            WatchDocuments(commandData.Application.Application);
            window.Show();
            return Result.Succeeded;
        }

        // V004 hardening: closing the document the window was opened for also closes the window, so it never
        // keeps a dead Document (and its external event) alive or offers actions on a closed model.
        private static void WatchDocuments(Autodesk.Revit.ApplicationServices.Application app)
        {
            if (_watchedApp != null) return;
            _watchedApp = app;
            _watchedApp.DocumentClosing += OnDocumentClosing;
        }

        private static void StopWatchingDocuments()
        {
            if (_watchedApp == null) return;
            try { _watchedApp.DocumentClosing -= OnDocumentClosing; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* Revit is shutting down */ }
            _watchedApp = null;
        }

        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            if (_window != null && _window.ViewModel.IsFor(e.Document)) _window.Close();
        }
    }
}
