using Autodesk.Revit.UI;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.ExternalEvents
{
    /// <summary>One handler + one ExternalEvent for the tool, created once (repo-wide pattern: Init() from the ViewModel).</summary>
    public static class InnerLoopDividerEventManager
    {
        public static InnerLoopDividerHandler Handler { get; private set; }
        public static ExternalEvent Event { get; private set; }

        public static void Init()
        {
            if (Handler != null) return;
            Handler = new InnerLoopDividerHandler();
            Event = ExternalEvent.Create(Handler);
        }
    }
}
