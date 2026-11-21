using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>
    /// Works out which section views one run covers: every section placed on the sheet in front of
    /// the user. Read-only.
    /// <para>
    /// The sheet is the active view when a sheet is open, and the sheet the active section sits on
    /// when a section is open. A section that is on no sheet is still offered on its own, so the
    /// tool is usable before the sections reach a sheet.
    /// </para>
    /// </summary>
    public static class SheetSectionCollector
    {
        /// <summary>
        /// Why <paramref name="view"/> cannot start a run, or null when it can: the tool needs a
        /// sheet with sections on it, or a section view.
        /// </summary>
        public static string CheckView(View view)
        {
            if (view == null)
                return "Open the sheet whose sections you want lines in, then run the tool again.";
            if (view.IsTemplate)
                return "The active view is a view template. Open a sheet, or a section view.";
            if (view is ViewSheet)
                return null;
            if (view is ViewSection section && IsSectionView(section))
                return null;
            return "Open a sheet that has sections on it, or a section view, then run the tool again. "
                 + "Detail lines belong to one view, so the tool draws them in the sections themselves.";
        }

        /// <summary>True for the section and detail views this tool draws in.</summary>
        public static bool IsSectionView(View view)
            => view is ViewSection && !view.IsTemplate
               && (view.ViewType == ViewType.Section || view.ViewType == ViewType.Detail);

        /// <summary>
        /// The sections to offer, in sheet order (by detail number, then by name), with the sheet
        /// they came from. <paramref name="sheetLabel"/> is empty when the sections came from the
        /// active view alone.
        /// </summary>
        public static List<SectionTarget> Collect(
            Document doc, View activeView, out SectionSourceKind source, out string sheetLabel)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            source = SectionSourceKind.Sheet;
            sheetLabel = string.Empty;

            ViewSheet sheet = ResolveSheet(doc, activeView);
            if (sheet == null)
            {
                // A section that is on no sheet: offer that one view by itself.
                source = SectionSourceKind.ActiveViewOnly;
                if (activeView is ViewSection lone && IsSectionView(lone))
                    return new List<SectionTarget> { Describe(doc, lone, null, null) };
                return new List<SectionTarget>();
            }

            sheetLabel = string.IsNullOrWhiteSpace(sheet.Name)
                ? sheet.SheetNumber
                : $"{sheet.SheetNumber} — {sheet.Name}";

            var targets = new List<SectionTarget>();
            foreach (ElementId viewportId in sheet.GetAllViewports())
            {
                if (doc.GetElement(viewportId) is not Viewport viewport) continue;
                if (doc.GetElement(viewport.ViewId) is not ViewSection section) continue;
                if (!IsSectionView(section)) continue;
                targets.Add(Describe(doc, section, viewport, sheet));
            }

            return targets
                .OrderBy(t => DetailNumberOrder(t.DetailNumber))
                .ThenBy(t => t.DetailNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.ViewName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The sheet the run belongs to: the active sheet, or the sheet the active section is
        /// placed on. Null when neither applies.
        /// </summary>
        public static ViewSheet ResolveSheet(Document doc, View activeView)
        {
            if (activeView is ViewSheet sheet) return sheet;
            if (activeView is not ViewSection section || !IsSectionView(section)) return null;

            foreach (Viewport viewport in new FilteredElementCollector(doc)
                         .OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (viewport.ViewId != section.Id) continue;
                return doc.GetElement(viewport.SheetId) as ViewSheet;
            }
            return null;
        }

        private static SectionTarget Describe(Document doc, ViewSection view, Viewport viewport, ViewSheet sheet)
        {
            var target = new SectionTarget
            {
                ViewKey = view.Id.Value,
                ViewName = view.Name,
                Scale = SafeScale(view),
                DetailNumber = viewport?.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? string.Empty,
                CropInactive = !SafeCropActive(view),
                LockReason = LockReason(doc, view, sheet)
            };
            return target;
        }

        /// <summary>
        /// Worksharing / ACC: why lines cannot be added to the view — it is owned by someone else,
        /// or changed in central since the last reload — or null when they can. Detail lines are
        /// owned by the view, so the view is what has to be available; the sheet is only reported
        /// when it is the thing that is locked.
        /// </summary>
        private static string LockReason(Document doc, View view, ViewSheet sheet)
        {
            if (!doc.IsWorkshared) return null;
            if (doc.IsReadOnly) return "the model is read-only";

            string reason = Lock(doc, view.Id, "the view");
            if (reason == null && sheet != null)
                reason = Lock(doc, sheet.Id, "sheet " + sheet.SheetNumber);
            return reason;
        }

        private static string Lock(Document doc, ElementId id, string what)
        {
            try
            {
                CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
                if (status == CheckoutStatus.OwnedByOtherUser)
                    return $"{what} is owned by {(string.IsNullOrWhiteSpace(owner) ? "another user" : owner)}";
                if (WorksharingUtils.GetModelUpdatesStatus(doc, id) == ModelUpdatesStatus.UpdatedInCentral)
                    return $"{what} changed in central — Reload Latest";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // A status Revit will not report is not treated as a lock; the transaction
                // would fail later and is reported then.
            }
            return null;
        }

        private static bool SafeCropActive(View view)
        {
            try { return view.CropBoxActive; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        private static int SafeScale(View view)
        {
            try { return view.Scale; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return 0; }
        }

        // "2" sorts before "10"; anything that is not a plain number sorts after the ones that are.
        private static int DetailNumberOrder(string detailNumber)
            => int.TryParse(detailNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : int.MaxValue;
    }
}
