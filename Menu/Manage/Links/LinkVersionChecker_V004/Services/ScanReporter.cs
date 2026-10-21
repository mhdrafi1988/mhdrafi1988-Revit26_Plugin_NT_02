using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    /// <summary>
    /// Progress + cancellation for long scans and batch actions that must stay on Revit's
    /// main thread. <see cref="StepAsync"/> reports progress and briefly yields to the WPF
    /// dispatcher so the progress bar repaints and the Cancel button can be clicked.
    /// </summary>
    public sealed class ScanReporter
    {
        private readonly Action<int, int, string> _report;
        private readonly Stopwatch _sinceYield = Stopwatch.StartNew();

        public CancellationToken Token { get; }

        public ScanReporter(Action<int, int, string> report, CancellationToken token)
        {
            _report = report;
            Token = token;
        }

        public static ScanReporter Silent => new ScanReporter(null, CancellationToken.None);

        public void Report(int done, int total, string text) => _report?.Invoke(done, total, text);

        public async Task StepAsync(int done, int total, string text)
        {
            Report(done, total, text);
            Token.ThrowIfCancellationRequested();
            if (_sinceYield.ElapsedMilliseconds >= 40)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                _sinceYield.Restart();
                Token.ThrowIfCancellationRequested();
            }
        }

        /// <summary>Awaits background work while keeping the UI responsive and the progress text fresh.</summary>
        public async Task WaitAsync(Task work, Func<string> text, Func<int> done, int total)
        {
            while (!work.IsCompleted)
            {
                Report(done(), total, text());
                await Task.WhenAny(work, Task.Delay(100));
            }
            await work;   // rethrows errors / cancellation
            Report(done(), total, text());
        }
    }
}
