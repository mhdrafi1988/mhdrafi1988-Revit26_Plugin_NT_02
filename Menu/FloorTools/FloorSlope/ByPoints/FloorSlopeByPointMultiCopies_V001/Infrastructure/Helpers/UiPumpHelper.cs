// File: UiPumpHelper.cs
// Location: Infrastructure/Helpers/
//
// NEW, per Rafi's confirmed progress-bar/Cancel decision (ported from V010).
//
// WHY THIS EXISTS: AutoSlopeByDrainWindow is shown modeless via window.Show()
// on Revit's own main thread (see AutoSlopeDrainCommand) — there is no
// separate STA thread for the WPF window. AutoSlopeDrainHandler.Execute()
// then runs on that SAME thread via ExternalEvent, so while a long
// synchronous call (chiefly DijkstraPathEngine.BuildGraph on a large floor)
// is running, WPF cannot repaint and cannot process input — a plain
// property-bound ProgressBar would not visibly move, and a queued
// Cancel-button click would just sit unprocessed until the call returns.
//
// DoEvents() is the standard WPF replacement for WinForms'
// Application.DoEvents(): it pushes a new DispatcherFrame and schedules a
// marker operation at Background priority. Because a pushed frame drains
// EVERY pending operation at or above that priority before the marker runs
// (this is what "priority" means for the dispatcher's own queue) — including
// any Render-priority layout/paint request our own property changes just
// scheduled, and any Input-priority click already delivered to our window's
// message queue — calling this right after updating progress properties is
// what actually gets the bar to repaint and gets a Cancel click to fire,
// without returning control to Revit's own command dispatching (Revit's API
// context already blocks reentrant command execution regardless of message
// pumping, so this is safe: it only pumps rendering/input for OUR window).
//
// Only call this from the same thread that owns the WPF window's dispatcher
// (i.e. from inside the ExternalEvent handler / engine progress callback,
// never from a background thread).

using System.Windows.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers
{
    public static class UiPumpHelper
    {
        public static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame),
                frame);
            Dispatcher.PushFrame(frame);
        }

        private static object ExitFrame(object frame)
        {
            ((DispatcherFrame)frame).Continue = false;
            return null;
        }
    }
}
