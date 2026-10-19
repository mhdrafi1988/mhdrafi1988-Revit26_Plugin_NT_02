#nullable enable
using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Static holder for one Handler + one ExternalEvent, created once.
    /// Per ARCHITECTURE.md §2, Init() is called from the ViewModel (the repo's real pattern),
    /// relying on the idempotent guard below.
    /// </summary>
    public static class RoofNameBuilderEventManager
    {
        public static RoofNameBuilderEventHandler? Handler { get; private set; }
        private static ExternalEvent? _event;

        public static void Init()
        {
            if (Handler != null) return; // idempotent guard
            Handler = new RoofNameBuilderEventHandler();
            _event = ExternalEvent.Create(Handler);
        }

        /// <summary>V002: returns Revit's answer so the caller can reset its busy state if the request was refused.</summary>
        public static ExternalEventRequest Raise() =>
            _event?.Raise() ?? ExternalEventRequest.Denied;
    }
}
