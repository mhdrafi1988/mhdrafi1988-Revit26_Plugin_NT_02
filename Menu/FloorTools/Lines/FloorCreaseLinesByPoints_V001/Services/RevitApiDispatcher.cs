using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Services
{
    /// <summary>
    /// Runs Revit API work from the modeless tool window inside a valid API context.
    /// The window is shown with Show(), so once FloorRidgeCommand.Execute returns, button
    /// clicks are no longer in API context and starting a Transaction (or calling
    /// PickObject) throws "Starting a transaction from an external application running
    /// outside of API context is not allowed". Work queued here is executed by Revit via
    /// an <see cref="ExternalEvent"/>, and the returned Task completes when it has run.
    /// </summary>
    public sealed class RevitApiDispatcher : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<(Action<UIApplication> Work, TaskCompletionSource<bool> Tcs)> _queue
            = new ConcurrentQueue<(Action<UIApplication>, TaskCompletionSource<bool>)>();

        private readonly ExternalEvent _event;

        /// <summary>Must be constructed inside API context (e.g. IExternalCommand.Execute).</summary>
        public RevitApiDispatcher()
        {
            _event = ExternalEvent.Create(this);
        }

        /// <summary>
        /// Queues <paramref name="work"/> to run in Revit's API context. Exceptions thrown by
        /// the work are rethrown to the awaiting caller.
        /// </summary>
        public Task InvokeAsync(Action<UIApplication> work)
        {
            // RunContinuationsAsynchronously: the caller's code after `await` must not run
            // inside Execute(), or it would silently depend on still being in API context.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue((work, tcs));

            var request = _event.Raise();
            if (request != ExternalEventRequest.Accepted && request != ExternalEventRequest.Pending)
            {
                // Drop everything still queued so no caller awaits forever.
                while (_queue.TryDequeue(out var item))
                    item.Tcs.TrySetException(new InvalidOperationException($"Revit rejected the request ({request})."));
            }
            return tcs.Task;
        }

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    // Esc / Cancel with a Yes-No confirm while this piece of Revit work runs.
                    using (ToolCancellation.BeginFor(ToolCatalog.FloorCreaseLinesByPoints, app))
                        item.Work(app);
                    item.Tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    // The awaiting view model sees a cancelled task and reports "Cancelled by user".
                    item.Tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    item.Tcs.TrySetException(ex);
                }
            }
        }

        public string GetName() => "Ridge By Points V057";

        public void Dispose()
        {
            while (_queue.TryDequeue(out var item))
                item.Tcs.TrySetCanceled();
            _event.Dispose();
        }
    }
}
