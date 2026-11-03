// =======================================================
// File: AutoSlopeDrainHandler.cs + AutoSlopeDrainEventManager
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.ExternalEvents
// Changes vs V012:
//   - No static mutable MultiPayload: one pending job per handler, and
//     Submit refuses a second job while one is pending.
//   - Submit checks ExternalEventRequest.Accepted (V012 ignored it, so a
//     refused Raise left the window stuck on "Running").
//   - Execute goes through ToolGuard.RunHandler.
//   - Init() is still called from the ViewModel, as everywhere in this repo
//     (ARCHITECTURE.md §2); the stale "call from OnStartup" note is gone.
// =======================================================

using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Infrastructure.ExternalEvents
{
    public sealed class AutoSlopeDrainHandler : IExternalEventHandler
    {
        private DrainJob _pending;

        public bool HasPending => _pending != null;

        public bool TrySetPending(DrainJob job)
        {
            if (_pending != null || job == null) return false;
            _pending = job;
            return true;
        }

        public void ClearPending() => _pending = null;

        public void Execute(UIApplication app)
        {
            DrainJob job = _pending;
            _pending = null;
            if (job == null) return;

            ToolGuard.RunHandler(GetType(), app, job.Execute);
        }

        public string GetName() => "Floor Slope By Drain V001";
    }

    public static class AutoSlopeDrainEventManager
    {
        public static AutoSlopeDrainHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new AutoSlopeDrainHandler();
            Event = ExternalEvent.Create(Handler);
        }

        /// <summary>Queues a job. False when another job is still pending or Revit refused the request.</summary>
        public static bool Submit(DrainJob job)
        {
            Init();
            if (!Handler.TrySetPending(job)) return false;

            ExternalEventRequest r = Event.Raise();
            if (r == ExternalEventRequest.Accepted) return true;

            Handler.ClearPending();
            return false;
        }
    }
}
