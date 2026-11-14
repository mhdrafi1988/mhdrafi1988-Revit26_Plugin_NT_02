using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure
{
    /// <summary>One view or sheet offered in the Scope list.</summary>
    public sealed class ViewEntry
    {
        /// <summary>Element id value of the view or sheet.</summary>
        public long Id { get; set; }

        /// <summary>Text shown in the list ("A-201 · Level 02 Plan", "L02 Floor Plan · 1:100").</summary>
        public string Name { get; set; }

        /// <summary>"Sheets" or "Views".</summary>
        public string Group { get; set; }
    }

    /// <summary>Lists the views and sheets the tool can scan and expands sheets to the views placed on them.</summary>
    public static class ViewCatalog
    {
        /// <summary>Group name for sheets.</summary>
        public const string SheetsGroup = "Sheets";

        /// <summary>Group name for views.</summary>
        public const string ViewsGroup = "Views";

        /// <summary>Names of the supported view kinds, for messages.</summary>
        public const string SupportedList =
            "floor, ceiling, structural and area plans, sections, elevations, detail and drafting views, legends and sheets";

        /// <summary>
        /// True when <paramref name="view"/> is a 2D view that holds annotations: not a template, not 3D,
        /// not a schedule or browser view.
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

        /// <summary>Scale used to convert to paper millimetres: 1 for sheets, otherwise the view scale (at least 1).</summary>
        public static int ScaleOf(View view)
        {
            if (view == null || view is ViewSheet)
                return 1;
            int scale;
            try { scale = view.Scale; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { scale = 1; }
            return scale < 1 ? 1 : scale;
        }

        /// <summary>Scale as text, "1:100".</summary>
        public static string ScaleText(View view) => "1:" + ScaleOf(view);

        /// <summary>Sheet number a view belongs to: the sheet's own number, or the sheet a view is placed on; empty if none.</summary>
        public static string SheetNumberOf(View view)
        {
            if (view is ViewSheet sheet)
                return sheet.SheetNumber ?? "";
            string number = view?.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER)?.AsString();
            return string.IsNullOrWhiteSpace(number) || number == "---" ? "" : number;
        }

        /// <summary>All sheets (by number) and all supported views (by name) of <paramref name="doc"/>.</summary>
        public static List<ViewEntry> List(Document doc)
        {
            var result = new List<ViewEntry>();
            if (doc == null || !doc.IsValidObject)
                return result;

            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsTemplate && !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber, System.StringComparer.OrdinalIgnoreCase);
            foreach (ViewSheet sheet in sheets)
                result.Add(new ViewEntry { Id = sheet.Id.Value, Name = sheet.SheetNumber + " · " + sheet.Name, Group = SheetsGroup });

            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !(v is ViewSheet) && IsSupported(v))
                .OrderBy(v => v.Name, System.StringComparer.OrdinalIgnoreCase);
            foreach (View view in views)
                result.Add(new ViewEntry { Id = view.Id.Value, Name = view.Name + " · " + ScaleText(view), Group = ViewsGroup });

            return result;
        }

        /// <summary>
        /// The views to scan for the chosen ids: each sheet followed by the supported views placed on it,
        /// then the directly chosen views. Every view appears once.
        /// </summary>
        public static List<View> Expand(Document doc, IEnumerable<long> chosenIds)
        {
            var result = new List<View>();
            var seen = new HashSet<long>();
            if (doc == null || chosenIds == null)
                return result;

            List<View> chosen = chosenIds
                .Select(id => doc.GetElement(new ElementId(id)) as View)
                .Where(IsSupported)
                .ToList();

            void Add(View view)
            {
                if (IsSupported(view) && seen.Add(view.Id.Value))
                    result.Add(view);
            }

            foreach (ViewSheet sheet in chosen.OfType<ViewSheet>())
            {
                Add(sheet);
                foreach (ElementId placedId in sheet.GetAllPlacedViews())
                    Add(doc.GetElement(placedId) as View);
            }
            foreach (View view in chosen.Where(v => !(v is ViewSheet)))
                Add(view);

            return result;
        }
    }
}
