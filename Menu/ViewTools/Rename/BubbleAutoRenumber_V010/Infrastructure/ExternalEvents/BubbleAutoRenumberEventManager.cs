using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Static holder for this tool's one handler and one ExternalEvent, created once
    /// per Revit session (ARCHITECTURE.md §2). Init() is idempotent and is called from
    /// the ViewModel constructor — which is what every tool in this repo actually does,
    /// whatever the older EventManager comments claim about OnStartup.
    ///
    /// CHANGED in V009: V008's Command built a handler and called
    /// ExternalEvent.Create per click, so a reopened window created a second event.
    /// </summary>
    internal static class BubbleAutoRenumberEventManager
    {
        public static BubbleAutoRenumberHandler Handler { get; private set; }

        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;

            Handler = new BubbleAutoRenumberHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
