using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Infrastructure
{
    /// <summary>
    /// Runs the actions queued by the modeless window inside Revit's API context, in order.
    /// Several requests can wait at once (for example a sheet read and a title block measure).
    /// </summary>
    public sealed class SheetViewsCombineHandler : IExternalEventHandler
    {
        private readonly Queue<Action<UIApplication>> _pending = new();

        /// <summary>Queues the action to run the next time the event is raised.</summary>
        public void Queue(Action<UIApplication> action)
        {
            lock (_pending) _pending.Enqueue(action);
        }

        /// <summary>Revit entry point — guarded so no exception reaches Revit.</summary>
        public void Execute(UIApplication app)
            => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        private void ExecuteUnguarded(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> action;
                lock (_pending)
                {
                    if (_pending.Count == 0) return;
                    action = _pending.Dequeue();
                }

                // Esc / Cancel with a Yes-No confirm while the queued Revit work runs; the run rolls back on cancel.
                using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.SheetViewsCombine, app);
                action.Invoke(app);
            }
        }

        /// <summary>Name shown by Revit for this handler.</summary>
        public string GetName() => "Sheet Views Combine V001";
    }
}
