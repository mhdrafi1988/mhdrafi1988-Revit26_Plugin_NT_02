using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Infrastructure.ExternalEvents
{
    public static class CurveDividerEventManager
    {
        public static CurveDividerHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new CurveDividerHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
