// =======================================================
// File: AutoSlopeHandler.cs + AutoSlopeEventManager
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.ExternalEvents
// Changes vs V030:
//   - No static mutable Payload: one pending job per handler, and Submit
//     refuses a second job while one is pending (plan 1.5).
//   - Execute goes through ToolGuard.RunHandler (plan 1.1).
//   - Init() is still called from the ViewModel, matching the repo-wide
//     pattern (ARCHITECTURE.md §2) — idempotent.
// =======================================================

using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.ExternalEvents
{
    public sealed class AutoSlopeHandler : IExternalEventHandler
    {
        private AutoSlopeJob _pending;

        public bool HasPending => _pending != null;

        public bool TrySetPending(AutoSlopeJob job)
        {
            if (_pending != null || job == null) return false;
            _pending = job;
            return true;
        }

        public void ClearPending() => _pending = null;

        public void Execute(UIApplication app)
        {
            AutoSlopeJob job = _pending;
            _pending = null;
            if (job == null) return;

            ToolGuard.RunHandler(GetType(), app, job.Execute);
        }

        public string GetName() => "AutoSlope By Point V034";
    }

    public static class AutoSlopeEventManager
    {
        public static AutoSlopeHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new AutoSlopeHandler();
            Event = ExternalEvent.Create(Handler);
        }

        /// <summary>Queues a job. False when another job is still pending or Revit refused the request.</summary>
        public static bool Submit(AutoSlopeJob job)
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
