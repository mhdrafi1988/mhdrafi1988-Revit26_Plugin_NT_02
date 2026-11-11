using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Helpers
{
    /// <summary>
    /// Which views V005 runs in: every 2D view that holds annotations. V004 ran in floor plans only.
    /// </summary>
    internal static class ViewSupport
    {
        /// <summary>Names of the supported view kinds, for the message shown in other views.</summary>
        public const string SupportedList =
            "floor, ceiling, structural and area plans, sections, elevations, detail and drafting views, legends and sheets";

        /// <summary>
        /// True when <paramref name="view"/> is a 2D view the tool can scan and resolve:
        /// not a template, not 3D / walkthrough, not a schedule or browser.
        /// </summary>
        public static bool IsSupported(View view)
        {
            if (view == null || !view.IsValidObject || view.IsTemplate)
                return false;
            if (view is View3D || view is ViewSchedule)
                return false;

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                case ViewType.Legend:
                case ViewType.DrawingSheet:
                    return true;
                default:
                    return false;
            }
        }
    }
}
