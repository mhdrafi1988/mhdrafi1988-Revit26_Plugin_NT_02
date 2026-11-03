using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Infrastructure.ExternalEvents
{
    public static class InnerLoopsAndPerpendicularEventManager
    {
        public static InnerLoopsAndPerpendicularHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new InnerLoopsAndPerpendicularHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
