using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.ExternalEvents
{
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
    }
}