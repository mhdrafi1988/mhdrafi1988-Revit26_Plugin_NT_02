using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>Entry check 1 — the active view must be a non-template plan view with a level.</summary>
    public static class PlanViewValidator
    {
        public const string NotAPlanMessage = "Open a plan view and run the tool again.";

        public static bool IsSupported(View? view, out ViewPlan? plan, out string message)
        {
            plan = null;
            message = NotAPlanMessage;

            if (view is not ViewPlan vp || vp.IsTemplate)
            {
                return false;
            }

            bool planType = vp.ViewType == ViewType.FloorPlan ||
                            vp.ViewType == ViewType.CeilingPlan ||
                            vp.ViewType == ViewType.EngineeringPlan ||
                            vp.ViewType == ViewType.AreaPlan;
            if (!planType)
            {
                return false;
            }

            if (vp.GenLevel == null)
            {
                message = "The active plan view has no associated level. " + NotAPlanMessage;
                return false;
            }

            plan = vp;
            message = string.Empty;
            return true;
        }
    }
}
