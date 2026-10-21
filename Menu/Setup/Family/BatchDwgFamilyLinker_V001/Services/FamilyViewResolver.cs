using Autodesk.Revit.DB;
using System.Linq;

namespace BatchDwgFamilyLinker.Services
{
    public static class FamilyViewResolver
    {
        public static ViewPlan GetPlanView(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.ViewType == ViewType.FloorPlan);
        }
    }
}
