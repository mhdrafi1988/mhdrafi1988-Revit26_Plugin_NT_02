using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Concurrent;

namespace Revit26_Plugin_Revised.BulkRename.V001.Core.Services
{
    /// <summary>
    /// Runs jobs queued by the WPF window inside the Revit API context, through one external event.
    /// Several jobs can be queued before Revit raises the event once, so every queued job runs, in order.
    /// Each job runs in its own try/catch, so one failure cannot strand the jobs behind it, and the busy
    /// count always goes back down. Create it in a Revit API context (for example while a command runs),
    /// because <see cref="ExternalEvent.Create"/> needs one.
    /// </summary>
    public sealed class RevitJobQueue : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<Job> _jobs = new();
        private readonly string _name;
        private readonly ExternalEvent _event;
        private int _busy;

        /// <summary>
        /// Raised when <see cref="IsBusy"/> changes. Revit runs external events on its main thread, which is
        /// also the thread of a modeless window opened by a command.
        /// </summary>
        public event EventHandler BusyChanged;

        /// <summary>True while at least one queued job has not finished.</summary>
        public bool IsBusy => _busy > 0;

        /// <summary>Creates the queue and its external event; <paramref name="name"/> is shown by Revit.</summary>
        public RevitJobQueue(string name)
        {
            _name = name ?? nameof(RevitJobQueue);
            _event = ExternalEvent.Create(this);
        }

        /// <summary>
        /// Queues <paramref name="work"/> to run in the Revit API context. Any exception it throws goes to
        /// <paramref name="onError"/>. Returns false, and the work never runs, when Revit refuses the request
        /// (for example while a modal dialog is open).
        /// </summary>
        public bool TryRun(Action<UIApplication> work, Action<Exception> onError)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            var job = new Job(work, onError);
            _jobs.Enqueue(job);
            SetBusy(+1);

            ExternalEventRequest request;
            try { request = _event.Raise(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { request = ExternalEventRequest.Denied; }

            // Pending: the event is already raised and this job will run with it.
            if (request == ExternalEventRequest.Accepted || request == ExternalEventRequest.Pending) return true;

            // Denied, TimedOut or anything else: Revit will not run it. The job stays queued but is skipped.
            job.Cancelled = true;
            SetBusy(-1);
            return false;
        }

        /// <summary>Releases the external event; queued jobs that have not run are dropped.</summary>
        public void Dispose()
        {
            try { _event.Dispose(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { /* Revit already released it */ }
        }

        void IExternalEventHandler.Execute(UIApplication app) => ToolGuard.RunHandler(GetType(), app, Drain);

        string IExternalEventHandler.GetName() => _name;

        private void Drain(UIApplication app)
        {
            while (_jobs.TryDequeue(out var job))
            {
                if (job.Cancelled) continue;

                try
                {
                    job.Work(app);
                }
                catch (Exception ex)
                {
                    Report(job, ex);
                }
                finally
                {
                    SetBusy(-1);
                }
            }
        }

        private void Report(Job job, Exception ex)
        {
            try
            {
                if (job.OnError != null) job.OnError(ex);
                else ToolGuard.Report(GetType(), ex);
            }
            catch (Exception reportFailure)
            {
                ToolGuard.Report(GetType(), reportFailure);
            }
        }

        private void SetBusy(int delta)
        {
            bool wasBusy = IsBusy;
            _busy = Math.Max(0, _busy + delta);
            if (wasBusy != IsBusy) BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Job
        {
            public Job(Action<UIApplication> work, Action<Exception> onError)
            {
                Work = work;
                OnError = onError;
            }

            public Action<UIApplication> Work { get; }

            public Action<Exception> OnError { get; }

            public bool Cancelled { get; set; }
        }
    }
}
