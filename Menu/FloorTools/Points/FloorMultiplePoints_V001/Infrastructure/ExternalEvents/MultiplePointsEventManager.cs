using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorMultiplePoints.V001.Infrastructure.ExternalEvents
{
    public static class MultiplePointsEventManager
    {
        public static MultiplePointsHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new MultiplePointsHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
