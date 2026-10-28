using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.Shared.Services
{
    /// <summary>
    /// Runs work from a modeless tool window inside the Revit API context, through one external event.
    /// <para>
    /// A modeless window's button clicks run outside the API context, where Revit refuses transactions
    /// ("Starting a transaction from an external application running outside of API context is not
    /// allowed") and picks in the view. Queue that work here instead: Revit runs it as soon as it is idle.
    /// </para>
    /// <para>
    /// Several jobs can be queued before Revit raises the event once, so every queued job runs, in order.
    /// Each job runs in its own try/catch, so one failure cannot strand the jobs behind it. Create the
    /// queue in a Revit API context (a command's Execute, or a constructor it runs), because
    /// <see cref="ExternalEvent.Create"/> needs one. Dispose it when the window closes.
    /// </para>
    /// Same design as the tool-local <c>RevitJobQueue</c> in Bulk Rename, Tracker ID and Isolated Section.
    /// </summary>
    public sealed class RevitApiQueue : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<Job> _jobs = new();
        private readonly string _name;
        private readonly ExternalEvent _event;
        private bool _disposed;

        /// <summary>Creates the queue and its external event; <paramref name="name"/> is shown by Revit.</summary>
        public RevitApiQueue(string name)
        {
            _name = name ?? nameof(RevitApiQueue);
            _event = ExternalEvent.Create(this);
        }

        /// <summary>
        /// Queues <paramref name="work"/> to run in the Revit API context. An exception it throws goes to
        /// <paramref name="onError"/>, or to <see cref="ToolGuard.Report"/> when that is null. Returns false,
        /// and the work never runs, when Revit refuses the request (for example while a modal dialog or an
        /// edit mode is open) or the queue is disposed.
        /// </summary>
        public bool TryRun(Action<UIApplication> work, Action<Exception> onError = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (_disposed) return false;

            var job = new Job(work, onError);
            _jobs.Enqueue(job);

            ExternalEventRequest request;
            try { request = _event.Raise(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { request = ExternalEventRequest.Denied; }

            // Pending: the event is already raised and this job will run with it.
            if (request == ExternalEventRequest.Accepted || request == ExternalEventRequest.Pending) return true;

            // Denied, TimedOut or anything else: Revit will not run it. The job stays queued but is skipped.
            job.Cancelled = true;
            return false;
        }

        /// <summary>Releases the external event; queued jobs that have not run are dropped. Safe on window close.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            while (_jobs.TryDequeue(out _)) { }
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
            }
        }

        private void Report(Job job, Exception ex)
        {
            try
            {
                if (job.OnError != null) job.OnError(ex);
                else ToolGuard.Report(job.Work.Method.DeclaringType ?? GetType(), ex);
            }
            catch (Exception reportFailure)
            {
                ToolGuard.Report(GetType(), reportFailure);
            }
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
