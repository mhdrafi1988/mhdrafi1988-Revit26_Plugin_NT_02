// =======================================================
// File: UiPumpHelper.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers
// Unchanged idea from V030 (per Rafi's confirmed progress/Cancel decision):
// the window and the ExternalEvent handler share Revit's main thread, so
// during a long synchronous run WPF can only repaint the progress bar, and
// a Cancel click can only be seen, if we pump the dispatcher.
// V032 change: the ViewModel throttles calls (~every 100 ms), and the
// window body is disabled while running, so pumped input can only reach
// Cancel and the log (B7).
// =======================================================

using System.Windows.Threading;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.Helpers
{
    public static class UiPumpHelper
    {
        public static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new DispatcherOperationCallback(f => { ((DispatcherFrame)f).Continue = false; return null; }),
                frame);
            Dispatcher.PushFrame(frame);
        }
    }
}
