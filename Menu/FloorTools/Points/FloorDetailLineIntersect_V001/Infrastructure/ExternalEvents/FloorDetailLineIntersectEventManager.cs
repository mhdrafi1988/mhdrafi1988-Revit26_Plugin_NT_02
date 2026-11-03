using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Infrastructure.ExternalEvents
{
    public static class FloorDetailLineIntersectEventManager
    {
        public static FloorDetailLineIntersectHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new FloorDetailLineIntersectHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
