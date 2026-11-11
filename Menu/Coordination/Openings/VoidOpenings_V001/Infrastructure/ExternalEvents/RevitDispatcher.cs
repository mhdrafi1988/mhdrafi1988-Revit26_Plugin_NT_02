using System;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.ExternalEvents
{
    /// <summary>
    /// <see cref="IRevitDispatcher"/> over a <see cref="RevitJobQueue"/>: the window awaits a task, the
    /// work runs inside Revit's external event. Create it in a Revit API context.
    /// </summary>
    public sealed class RevitDispatcher : IRevitDispatcher, IDisposable
    {
        private readonly RevitJobQueue _queue;
        private readonly RevitContext _context;

        /// <summary>Creates the dispatcher and its external event.</summary>
        public RevitDispatcher(RevitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _queue = new RevitJobQueue("Void Openings");
        }

        /// <inheritdoc/>
        public Task<T> RunAsync<T>(Func<T> work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            // Continuations run asynchronously so the awaiting window code never runs inside the external event.
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool queued = _queue.TryRun(
                app =>
                {
                    _context.Application = app;
                    completion.TrySetResult(work());
                },
                error => completion.TrySetException(error));

            if (!queued)
                completion.TrySetException(new InvalidOperationException(
                    "Revit is busy and could not run the request. Close any open Revit dialog or command, then try again."));

            return completion.Task;
        }

        /// <summary>Releases the external event.</summary>
        public void Dispose() => _queue.Dispose();
    }
}
