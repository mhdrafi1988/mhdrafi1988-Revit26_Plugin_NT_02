using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Controls;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// User cancel for one tool run. Revit runs the API on its single main thread and the tool windows
    /// live on that same thread, so while a command or external-event handler is busy WPF cannot
    /// deliver key presses or button clicks. A run therefore calls <see cref="Check"/> inside its
    /// processing loops: every ~150 ms it pumps the WPF dispatcher (so the progress bar repaints and a
    /// Cancel click arrives), polls the Escape key while Revit is the foreground application, and when
    /// the user asked to cancel it shows "Cancel the process? Yes / No". Yes throws
    /// <see cref="OperationCanceledException"/>, which unwinds the run; the run's outer
    /// <c>TransactionGroup</c> is then rolled back and <see cref="ToolGuard"/> treats the exception as a
    /// user cancel, not an error. No clears the request and the run continues.
    /// <para>
    /// Create one per run with <see cref="Begin"/> or <see cref="BeginWithProgressWindow"/>, on the Revit
    /// main thread, and dispose it in a <c>finally</c> block. A single long Revit API call cannot be
    /// interrupted; place <see cref="Check"/> between such calls.
    /// </para>
    /// </summary>
    public sealed class ToolCancellation : IDisposable
    {
        private const int VkEscape = 0x1B;
        private const long CheckIntervalMs = 150;

        private const string ConfirmText =
            "Cancel the process?\nAll changes made by this run will be undone. Files already exported are kept.";

        private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

        // The run currently owned by each window, so the shell's Cancel button can find it
        // without every tool wiring a command.
        private static readonly Dictionary<Window, ToolCancellation> ActiveByWindow = new();

        private readonly ToolInfo _tool;
        private readonly Window _owner;
        private readonly Action<double, string> _progress;
        private readonly Dispatcher _dispatcher;
        private readonly CancellationTokenSource _cts = new();
        private readonly Stopwatch _sinceCheck = Stopwatch.StartNew();
        private readonly ToolProgressWindow _progressWindow;
        private readonly ToolWindowShell _shell;

        // Open tool windows (registered by ToolGuard when a window loads), so BeginFor can find a tool's window.
        private static readonly List<Window> OpenToolWindows = new();

        // The innermost active run on the Revit thread, so deep engine code can call CheckCurrent()
        // without every method taking a ToolCancellation parameter.
        private static ToolCancellation _current;
        private readonly ToolCancellation _previous;
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private readonly bool _drivesShell;
        private Action<double, string> _shellProgress;
        private bool _wasRunning;

        private bool _requested;
        private bool _asking;
        private bool _escapeWasDown;
        private bool _closeOwnerWhenUnwound;
        private bool _disposed;

        private ToolCancellation(ToolInfo tool, Window owner, Action<double, string> progress,
            ToolProgressWindow progressWindow, bool drivesShell = false)
        {
            _previous = _current;
            _current = this;
            _drivesShell = drivesShell;
            _tool = tool;
            _owner = owner;
            _progress = progress;
            _progressWindow = progressWindow;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _escapeWasDown = IsEscapeDown();

            if (_owner != null)
            {
                ActiveByWindow[_owner] = this;
                _shell = ToolWindowShell.FindIn(_owner);
                if (_shell != null)
                {
                    _shell.IsCancellable = true;
                    if (_drivesShell)
                    {
                        // SetCurrentValue keeps the view model's bindings on these properties intact.
                        _wasRunning = _shell.IsRunning; // a tool whose run spans several event calls keeps its own state
                        _shell.SetCurrentValue(ToolWindowShell.IsRunningProperty, true);
                        _shell.SetCurrentValue(ToolWindowShell.ProgressProperty, 0.0);
                        _shellProgress = (percent, text) =>
                        {
                            _shell.SetCurrentValue(ToolWindowShell.ProgressProperty, percent);
                            _shell.SetCurrentValue(ToolWindowShell.SummaryTextProperty, text ?? string.Empty);
                        };
                    }
                }
                _owner.PreviewKeyDown += OnOwnerPreviewKeyDown;
                _owner.Closing += OnOwnerClosing;
            }
        }

        /// <summary>
        /// Starts cancel handling for a run without touching the tool's view model: finds the tool's open
        /// window (so Esc, the window's Cancel button and window-close work), shows running state and
        /// progress in the window's shell, and when the tool has no open window shows the small
        /// <see cref="ToolProgressWindow"/> instead (needs <paramref name="app"/>).
        /// Call on the Revit main thread and dispose in a <c>using</c>.
        /// </summary>
        /// <param name="tool">Tool entry from <see cref="ToolCatalog"/>.</param>
        /// <param name="app">Revit application; used to own the progress pop-up when the tool has no window. May be null.</param>
        /// <param name="initialText">Pop-up text before the first <see cref="Report"/>.</param>
        public static ToolCancellation BeginFor(ToolInfo tool, UIApplication app = null, string initialText = "Working…")
        {
            Window window = FindWindow(tool);
            if (window != null) return new ToolCancellation(tool, window, null, null, drivesShell: true);
            if (app != null) return BeginWithProgressWindow(tool, app, initialText);
            return new ToolCancellation(tool, null, null, null);
        }

        /// <summary>Called by <see cref="ToolGuard"/> when a tool window loads; the window is forgotten when it closes.</summary>
        internal static void RegisterWindow(Window window)
        {
            if (window == null || OpenToolWindows.Contains(window)) return;
            OpenToolWindows.Add(window);
            window.Closed += (_, _) => OpenToolWindows.Remove(window);
        }

        private static Window FindWindow(ToolInfo tool)
        {
            if (tool == null) return null;
            for (int i = OpenToolWindows.Count - 1; i >= 0; i--)
                if (ReferenceEquals(ToolCatalog.Find(OpenToolWindows[i].GetType()), tool)) return OpenToolWindows[i];
            return null;
        }

        /// <summary>
        /// Checks the innermost active run (see <see cref="Check"/>); does nothing when no run is active.
        /// Off the Revit thread it only throws if the user already confirmed a cancel. Lets engine code deep
        /// in a tool call it without being handed the run.
        /// </summary>
        public static void CheckCurrent() => _current?.Check();

        /// <summary>Reports progress on the innermost active run (see <see cref="Report"/>); does nothing when none is active.</summary>
        public static void ReportCurrent(int done, int total, string text) => _current?.Report(done, total, text);

        /// <summary>
        /// Starts cancel handling for a run on a tool window. Call on the Revit main thread, before the
        /// processing loops, and dispose in a <c>finally</c> block.
        /// </summary>
        /// <param name="tool">Tool entry from <see cref="ToolCatalog"/>; its title names the confirm dialog.</param>
        /// <param name="owner">The tool window: owner of the Yes/No dialog, source of Escape and window-close requests. May be null.</param>
        /// <param name="progress">Optional sink for <see cref="Report"/> (percent 0–100, text), typically the view model's Progress and SummaryText.</param>
        public static ToolCancellation Begin(ToolInfo tool, Window owner, Action<double, string> progress = null) =>
            new(tool, owner, progress, null);

        /// <summary>
        /// Starts cancel handling for a tool that has no window: shows a small modeless
        /// <see cref="ToolProgressWindow"/> (progress text, bar, Cancel button) owned by the Revit main
        /// window. The pop-up closes when the returned object is disposed.
        /// </summary>
        /// <param name="tool">Tool entry from <see cref="ToolCatalog"/>.</param>
        /// <param name="app">Revit application; its main window owns the pop-up.</param>
        /// <param name="initialText">Text shown before the first <see cref="Report"/>.</param>
        public static ToolCancellation BeginWithProgressWindow(ToolInfo tool, UIApplication app, string initialText = "Working…")
        {
            var window = new ToolProgressWindow(tool?.Title ?? "SloperPro", initialText);
            if (app != null)
                new WindowInteropHelper(window).Owner = app.MainWindowHandle;
            window.Show();
            return new ToolCancellation(tool, window, window.Update, window);
        }

        /// <summary>
        /// Asks the run that <paramref name="window"/> owns to cancel (as if Escape were pressed).
        /// Returns false when no run is active on that window.
        /// </summary>
        public static bool RequestFor(Window window)
        {
            if (window == null || !ActiveByWindow.TryGetValue(window, out var run)) return false;
            run.RequestCancel();
            return true;
        }

        /// <summary>True while the innermost active run has a confirmed user cancel (usable in <c>finally</c> blocks while the cancel unwinds).</summary>
        public static bool CurrentIsCancelled => _current?.IsCancelled == true;

        /// <summary>
        /// Message for a completion callback that found no result: the cancel text when the user cancelled,
        /// otherwise <paramref name="fallback"/>.
        /// </summary>
        public static string CancelledOr(string fallback, string cancelled = "Cancelled by user. Nothing was changed.") =>
            CurrentIsCancelled ? cancelled : fallback;

        /// <summary>
        /// Asks the active run of <paramref name="tool"/> to cancel, finding the tool's open window.
        /// For tool windows that keep their own Cancel button: the button's command calls this.
        /// Returns false when that tool has no active run.
        /// </summary>
        public static bool RequestFor(ToolInfo tool) => RequestFor(FindWindow(tool));

        /// <summary>Token for work that runs on background threads; cancelled when the user answers Yes. Never call the Revit API from a background thread.</summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// Text of the Yes/No dialog. Tools whose changes cannot be rolled back (for example link load/unload)
        /// set this to say what stays; the default says all changes are undone.
        /// </summary>
        public string ConfirmMessage { get; set; } = ConfirmText;

        /// <summary>True once the user confirmed the cancel.</summary>
        public bool IsCancelled => _cts.IsCancellationRequested;

        /// <summary>Items finished at the last <see cref="Report"/> (for "Cancelled: 37 of 120 files exported").</summary>
        public int LastDone { get; private set; }

        /// <summary>Total items at the last <see cref="Report"/>.</summary>
        public int LastTotal { get; private set; }

        /// <summary>
        /// Records a cancel request. Called by the Cancel button, the window's Escape key and window
        /// closing; the Yes/No dialog appears at the next <see cref="Check"/>.
        /// </summary>
        public void RequestCancel()
        {
            if (!_cts.IsCancellationRequested) _requested = true;
        }

        /// <summary>Updates the progress bar and summary text (percent is computed from the counts).</summary>
        public void Report(int done, int total, string text)
        {
            LastDone = done;
            LastTotal = total;
            double percent = total <= 0 ? 0 : Math.Min(100.0, 100.0 * done / total);
            if (Environment.CurrentManagedThreadId != _threadId) return;
            try { (_progress ?? _shellProgress)?.Invoke(percent, text ?? string.Empty); }
            catch { /* a UI binding failure must not stop the run */ }
        }

        /// <summary><see cref="Report"/> followed by <see cref="Check"/>: the usual one-liner at the top of a loop body.</summary>
        public void Step(int done, int total, string text)
        {
            Report(done, total, text);
            Check();
        }

        /// <summary>
        /// Call inside every processing loop, between phases and before each commit. Cheap: it does real
        /// work only every ~150 ms. Pumps the dispatcher, polls Escape (only while Revit is the foreground
        /// application) and, when cancel was requested, asks the user. Throws
        /// <see cref="OperationCanceledException"/> after a Yes.
        /// </summary>
        public void Check()
        {
            if (_disposed) return;
            if (Environment.CurrentManagedThreadId == _threadId && !_cts.IsCancellationRequested && _sinceCheck.ElapsedMilliseconds >= CheckIntervalMs)
            {
                _sinceCheck.Restart();
                PumpDispatcher();
                PollEscape();
                if (_requested && !_asking) AskUser();
            }
            _cts.Token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// For work started with <c>Task.Run</c>: keeps the Revit thread pumping and checking until
        /// <paramref name="task"/> finishes. After a Yes the token is cancelled, this method throws and
        /// the task is left to stop on its own (it must watch <see cref="Token"/>).
        /// </summary>
        public void WaitFor(Task task)
        {
            try
            {
                while (!task.IsCompleted)
                {
                    Check();
                    task.Wait(25);
                }
            }
            catch (OperationCanceledException)
            {
                task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                throw;
            }
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Puts a shell property back under its view-model binding (re-reads the source), or, when the
        /// property is not bound, sets <paramref name="fallback"/> (nothing when it is null).
        /// </summary>
        private void RestoreShellValue(DependencyProperty property, object fallback)
        {
            var binding = System.Windows.Data.BindingOperations.GetBindingExpressionBase(_shell, property);
            if (binding != null) binding.UpdateTarget();
            else if (fallback != null) _shell.SetCurrentValue(property, fallback);
        }

        /// <summary>Ends cancel handling: detaches from the window and closes the progress pop-up. If the user cancelled while closing the window, the window closes now.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_owner != null)
            {
                _owner.PreviewKeyDown -= OnOwnerPreviewKeyDown;
                _owner.Closing -= OnOwnerClosing;
                if (_shell != null)
                {
                    _shell.IsCancellable = false;
                    if (_drivesShell)
                    {
                        // Hand the strip back to the view model. Restoring the value seen at the start
                        // left the bar spinning for tools that set IsRunning before raising the event and
                        // cleared it in their completion callback (which runs before this Dispose).
                        RestoreShellValue(ToolWindowShell.IsRunningProperty, _wasRunning);
                        RestoreShellValue(ToolWindowShell.ProgressProperty, null);
                        RestoreShellValue(ToolWindowShell.SummaryTextProperty, null);
                        if (_cts.IsCancellationRequested)
                            _shell.SetCurrentValue(ToolWindowShell.SummaryTextProperty, "Cancelled by user.");
                    }
                }
                if (ActiveByWindow.TryGetValue(_owner, out var current) && ReferenceEquals(current, this))
                    ActiveByWindow.Remove(_owner);
            }

            _progressWindow?.CloseFromHost();
            if (ReferenceEquals(_current, this)) _current = _previous;
            if (_cts.IsCancellationRequested && _owner?.DataContext is Revit26_Plugin_Revised.Shared.ViewModels.ToolViewModelBase vm)
                vm.NotifyCancelled(LastDone, LastTotal);

            if (_closeOwnerWhenUnwound && _owner != null && _progressWindow == null)
            {
                Window owner = _owner;
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => owner.Close()));
            }
            // _cts is left undisposed on purpose: it holds no timers or handles, and callers read
            // IsCancelled / Token after the run has unwound.
        }

        // ── internals ─────────────────────────────────────────────────────────────

        private void OnOwnerPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true; // while running, Escape means "cancel", never "close the window"
            RequestCancel();
        }

        private void OnOwnerClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_disposed) return;
            // Closing mid-run: keep the window until the run has been asked and unwound.
            e.Cancel = true;
            _closeOwnerWhenUnwound = true;
            RequestCancel();
        }

        private void AskUser()
        {
            _asking = true;
            try
            {
                MessageBoxResult answer = (_owner != null && _owner.IsLoaded)
                    ? MessageBox.Show(_owner, ConfirmMessage, _tool?.Title ?? "SloperPro", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    : MessageBox.Show(ConfirmMessage, _tool?.Title ?? "SloperPro", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

                _requested = false;
                _escapeWasDown = IsEscapeDown();
                if (answer == MessageBoxResult.Yes)
                    _cts.Cancel();
                else
                    _closeOwnerWhenUnwound = false; // user chose to continue, so a pending window close is dropped
            }
            finally
            {
                _asking = false;
            }
        }

        private void PollEscape()
        {
            if (!IsRevitForeground()) { _escapeWasDown = false; return; }

            short state = GetAsyncKeyState(VkEscape);
            bool down = (state & 0x8000) != 0;
            bool pressedSinceLastCall = (state & 0x0001) != 0;
            if ((down || pressedSinceLastCall) && !_escapeWasDown)
                _requested = true;
            _escapeWasDown = down; // wait for release so one press triggers once
        }

        private static bool IsEscapeDown() => (GetAsyncKeyState(VkEscape) & 0x8000) != 0;

        private static bool IsRevitForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            GetWindowThreadProcessId(foreground, out uint processId);
            return processId == CurrentProcessId;
        }

        private void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static object ExitFrame(object frame)
        {
            ((DispatcherFrame)frame).Continue = false;
            return null;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
