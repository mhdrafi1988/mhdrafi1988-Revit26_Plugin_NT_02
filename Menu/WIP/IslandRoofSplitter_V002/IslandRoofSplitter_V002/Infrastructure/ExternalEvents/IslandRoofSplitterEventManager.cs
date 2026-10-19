// IslandRoofSplitter V002 — static holder for one handler + one ExternalEvent, created once.
// Init() is idempotent and, per the actual repo-wide pattern (ARCHITECTURE.md §2), is called from the ViewModel.
#nullable enable
using Autodesk.Revit.UI;

namespace Revit26_Plugin.IslandRoofSplitter.V002.Infrastructure.ExternalEvents
{
    public static class IslandRoofSplitterEventManager
    {
        public static IslandRoofSplitterEventHandler? Handler { get; private set; }
        public static ExternalEvent? Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new IslandRoofSplitterEventHandler();
            Event = ExternalEvent.Create(Handler);
        }

        /// <returns>False if Revit refused the request (e.g. another one is still pending).</returns>
        public static bool Raise(SplitterRequest request)
        {
            Init();
            if (Handler!.Request != null) return false; // previous request not yet executed
            Handler.Request = request;
            var r = Event!.Raise();
            if (r != ExternalEventRequest.Accepted) { Handler.Request = null; return false; }
            return true;
        }
    }
}
