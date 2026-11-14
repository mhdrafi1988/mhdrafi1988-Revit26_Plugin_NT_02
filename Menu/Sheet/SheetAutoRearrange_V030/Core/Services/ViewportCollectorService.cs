using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Services
{
    /// <summary>
    /// Reads all Viewport elements placed on a given sheet and builds the
    /// ViewOnSheetItem rows the grid binds to. Read-only — no transaction
    /// required, safe to call from the ViewModel constructor / on load.
    /// </summary>
    public class ViewportCollectorService
    {
        private const double FeetToMm = 304.8;

        /// <summary>
        /// Collects every viewport on <paramref name="sheet"/> and returns one
        /// ViewOnSheetItem per viewport, in no particular order (grid handles sort).
        /// </summary>
        public List<ViewOnSheetItem> CollectViewsOnSheet(Document doc, ViewSheet sheet)
        {
            var items = new List<ViewOnSheetItem>();

            var viewportIds = sheet.GetAllViewports();
            foreach (var vpId in viewportIds)
            {
                if (doc.GetElement(vpId) is not Viewport viewport)
                    continue;

                var view = doc.GetElement(viewport.ViewId) as View;
                if (view == null)
                    continue;

                var box = viewport.GetBoxOutline();
                double widthMm = (box.MaximumPoint.X - box.MinimumPoint.X) * FeetToMm;
                double heightMm = (box.MaximumPoint.Y - box.MinimumPoint.Y) * FeetToMm;
                XYZ center = (box.MaximumPoint + box.MinimumPoint) * 0.5;

                items.Add(new ViewOnSheetItem(
                    viewportId: vpId,
                    viewId: view.Id,
                    viewName: view.Name,
                    viewType: view.ViewType,
                    viewTypeName: view.ViewType.ToString(),
                    scaleDisplay: FormatScale(view),
                    widthMm: widthMm,
                    heightMm: heightMm,
                    currentCenter: center));
            }

            return items;
        }

        /// <summary>
        /// "1:N" for views with a Scale property; "—" for schedules, legends,
        /// and any other view type where Scale doesn't apply.
        /// ASSUMPTION: uses View.Scale (int) directly per Rafi's confirmation —
        /// no shared formatting helper existed to port from.
        /// </summary>
        private string FormatScale(View view)
        {
            if (view.ViewType == ViewType.Schedule || view.ViewType == ViewType.Legend)
                return "—";

            try
            {
                int scale = view.Scale;
                return scale > 0 ? $"1:{scale}" : "—";
            }
            catch
            {
                // View.Scale throws for a handful of view types that don't support it.
                return "—";
            }
        }
    }
}
