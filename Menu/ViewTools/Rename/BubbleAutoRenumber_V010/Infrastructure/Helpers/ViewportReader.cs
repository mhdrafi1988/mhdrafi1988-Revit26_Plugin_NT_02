using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Infrastructure.Helpers
{
    /// <summary>
    /// Reads a sheet's viewports into Core's plain BubbleRow data — the only place
    /// Autodesk's ViewType is translated into this tool's BubbleViewKind, so that
    /// Core/ stays free of the Revit API (ARCHITECTURE.md §3).
    ///
    /// CHANGED in V009. V008 collected like this:
    ///
    ///     .Select(vp =&gt; new SectionEntry { View = doc.GetElement(vp.ViewId) as ViewSection, … })
    ///     .Where(e =&gt; e.View != null &amp;&amp; e.View.ViewType == ViewType.Section)
    ///
    /// Both halves excluded drafting views: ViewDrafting is not a ViewSection, so the
    /// cast returned null before the ViewType test was ever reached. V009 reads every
    /// viewport as a View, records what kind it is, and lets RenumberOptions decide
    /// what is in scope.
    /// </summary>
    internal static class ViewportReader
    {
        /// <summary>Every viewport on the sheet, as Core data. Read-only; no transaction needed.</summary>
        public static List<BubbleRow> Read(Document doc, ViewSheet sheet)
        {
            var rows = new List<BubbleRow>();

            if (doc == null || sheet == null) return rows;

            IEnumerable<Viewport> viewports = new FilteredElementCollector(doc, sheet.Id)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>();

            foreach (Viewport vp in viewports)
            {
                if (doc.GetElement(vp.ViewId) is not View view) continue;

                Outline box = vp.GetBoxOutline();
                XYZ centre = (box.MinimumPoint + box.MaximumPoint) / 2.0;

                Parameter number = view.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                (BubbleViewKind kind, string label, string pill) = Classify(view.ViewType);

                rows.Add(new BubbleRow
                {
                    ViewId = view.Id.Value,
                    ViewportId = vp.Id.Value,
                    Kind = kind,
                    KindLabel = label,
                    PillGroup = pill,
                    ViewName = view.Name,
                    CurrentNumber = number?.AsString() ?? string.Empty,
                    HasNumberParameter = number != null,
                    IsWritable = number != null && !number.IsReadOnly,

                    // Bottom-alignment banding, as in V008 — a tall section and a short
                    // one sitting on the same guideline belong to the same row.
                    BottomY = box.MinimumPoint.Y,
                    CenterX = centre.X
                });
            }

            return rows;
        }

        /// <summary>
        /// ViewType → (kind for filtering, label for the Type pill, palette key for
        /// Shared/Converters.cs ViewTypeGroupToBrushConverter). The palette key must
        /// match that converter's dictionary by name; anything it does not know falls
        /// back to its neutral grey pill, which is why unusual types pass an empty key
        /// rather than inventing one.
        /// </summary>
        private static (BubbleViewKind Kind, string Label, string Pill) Classify(ViewType type) => type switch
        {
            ViewType.Section => (BubbleViewKind.Section, "Section", "SectionOrCallout"),
            ViewType.Detail => (BubbleViewKind.Detail, "Detail", "SectionOrCallout"),
            ViewType.DraftingView => (BubbleViewKind.Drafting, "Drafting", "Drafting"),
            ViewType.Elevation => (BubbleViewKind.Elevation, "Elevation", "Elevation"),

            ViewType.FloorPlan => (BubbleViewKind.Other, "Floor Plan", "FloorPlan"),
            ViewType.CeilingPlan => (BubbleViewKind.Other, "Ceiling Plan", "CeilingPlan"),
            ViewType.EngineeringPlan => (BubbleViewKind.Other, "Structural Plan", "StructuralPlan"),
            ViewType.AreaPlan => (BubbleViewKind.Other, "Area Plan", "AreaPlan"),
            ViewType.Legend => (BubbleViewKind.Other, "Legend", "Legend"),
            ViewType.Schedule => (BubbleViewKind.Other, "Schedule", "Schedule"),
            ViewType.ColumnSchedule => (BubbleViewKind.Other, "Schedule", "Schedule"),
            ViewType.PanelSchedule => (BubbleViewKind.Other, "Schedule", "Schedule"),
            ViewType.ThreeD => (BubbleViewKind.Other, "3D", string.Empty),
            ViewType.Rendering => (BubbleViewKind.Other, "Rendering", string.Empty),
            ViewType.Walkthrough => (BubbleViewKind.Other, "Walkthrough", string.Empty),

            _ => (BubbleViewKind.Other, type.ToString(), string.Empty)
        };
    }
}
