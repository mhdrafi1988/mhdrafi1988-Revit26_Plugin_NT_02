using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.ExternalEvents
{
    public static class FloorComparisonEventManager
    {
        public static FloorComparisonHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new FloorComparisonHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
