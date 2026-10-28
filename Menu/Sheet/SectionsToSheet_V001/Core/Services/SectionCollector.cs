using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout;
using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Services
{
    /// <summary>
    /// Reads the sections on offer into a <see cref="SourceSnapshot"/>: the ones whose marker is
    /// visible in the source view, the selected ones, and any extra keys asked for. Read-only.
    /// <para>
    /// A section's marker in a view is a "viewer" element (category Views / OST_Viewers). Each
    /// section view lists its viewers among its dependent elements, which is how a marker is
    /// mapped back to its view.
    /// </para>
    /// </summary>
    public static class SectionCollector
    {
        private static readonly ElementCategoryFilter ViewerFilter = new(BuiltInCategory.OST_Viewers);

        /// <summary>
        /// Why <paramref name="view"/> cannot be the source view, or null when it can: it must be
        /// a plan, section, elevation or detail view that can show section markers.
        /// </summary>
        public static string CheckView(View view)
        {
            if (view == null)
                return "Open the view that shows the section markers, then run the tool again.";
            if (view.IsTemplate)
                return "The active view is a view template. Open a view that shows the section markers.";
            return view.ViewType switch
            {
                ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan
                    or ViewType.Section or ViewType.Elevation or ViewType.Detail => null,
                _ => "Open a plan, section, elevation or detail view that shows the section markers, then run the tool again."
            };
        }

        /// <summary>True for section and detail-section views that can go on a sheet.</summary>
        public static bool IsSectionView(View view)
            => view is ViewSection && !view.IsTemplate && (view.ViewType == ViewType.Section || view.ViewType == ViewType.Detail);

        /// <summary>
        /// Reads the snapshot. <paramref name="selection"/> is the current Revit selection;
        /// <paramref name="extraKeys"/> are more section views to include (picked or ticked ones).
        /// </summary>
        public static SourceSnapshot Read(Document doc, View source, ICollection<ElementId> selection, IEnumerable<long> extraKeys)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (source == null) throw new ArgumentNullException(nameof(source));

            var viewerToView = BuildViewerIndex(doc);

            // Markers visible in the source view.
            var markers = new Dictionary<long, XYZ>();
            var inView = new List<long>();
            foreach (Element viewer in new FilteredElementCollector(doc, source.Id)
                         .OfCategory(BuiltInCategory.OST_Viewers).WhereElementIsNotElementType())
            {
                var view = ViewFor(viewer, viewerToView);
                if (view == null || view.Id == source.Id)
                    continue;
                long key = view.Id.Value;
                if (!inView.Contains(key))
                    inView.Add(key);
                if (!markers.ContainsKey(key) && MarkerPoint(viewer, source) is XYZ p)
                    markers[key] = p;
            }

            // The Revit selection: markers in a view, or section views in the Project Browser.
            var selected = new List<long>();
            int ignored = 0;
            foreach (ElementId id in selection ?? Array.Empty<ElementId>())
            {
                Element element = doc.GetElement(id);
                var view = element == null ? null : ViewFor(element, viewerToView);
                if (view == null)
                {
                    ignored++;
                    continue;
                }
                long key = view.Id.Value;
                if (!selected.Contains(key))
                    selected.Add(key);
                if (!markers.ContainsKey(key) && element is not View && MarkerPoint(element, source) is XYZ p)
                    markers[key] = p;
            }

            var keys = new List<long>(inView);
            foreach (long key in selected.Concat(extraKeys ?? Array.Empty<long>()))
                if (!keys.Contains(key))
                    keys.Add(key);

            var viewports = new Dictionary<ElementId, Viewport>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
                viewports[vp.ViewId] = vp;

            var inViewSet = new HashSet<long>(inView);
            var sections = new Dictionary<long, SectionCandidate>();
            var sheets = new Dictionary<long, SheetContent>();
            XYZ right = source.RightDirection, up = source.UpDirection;

            foreach (long key in keys)
            {
                if (doc.GetElement(new ElementId(key)) is not ViewSection view || !IsSectionView(view))
                    continue;

                viewports.TryGetValue(view.Id, out Viewport viewport);
                ViewSheet sheet = viewport == null ? null : doc.GetElement(viewport.SheetId) as ViewSheet;
                markers.TryGetValue(key, out XYZ point);

                if (sheet != null && !sheets.ContainsKey(sheet.Id.Value))
                    sheets[sheet.Id.Value] = ReadSheet(doc, sheet);

                sections[key] = new SectionCandidate
                {
                    Key = key,
                    Name = view.Name,
                    Scale = SafeScale(view),
                    MarkerAcross = point?.DotProduct(right),
                    MarkerUp = point?.DotProduct(up),
                    InSourceView = inViewSet.Contains(key),
                    CurrentSheetKey = sheet?.Id.Value ?? 0,
                    CurrentSheetNumber = sheet?.SheetNumber,
                    CurrentSheetName = sheet?.Name,
                    LockReason = LockReason(doc, view, viewport, sheet)
                };
            }

            var (viewportTypes, defaultViewportType) = ReadViewportTypes(doc, viewports.Values.FirstOrDefault());

            return new SourceSnapshot
            {
                SourceViewKey = source.Id.Value,
                SourceViewName = source.Name,
                SourceViewScale = Math.Max(1, SafeScale(source)),
                InViewKeys = inView.Where(sections.ContainsKey).ToList(),
                SelectionKeys = selected.Where(sections.ContainsKey).ToList(),
                IgnoredSelectionCount = ignored,
                Sections = sections,
                Sheets = sheets,
                ExistingSheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Select(s => s.SheetNumber).Where(n => !string.IsNullOrEmpty(n)).ToList(),
                TitleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Select(s => new NamedType(s.Id.Value, $"{s.FamilyName} : {s.Name}"))
                    .OrderBy(t => t.Name, NaturalStringComparer.Instance).ToList(),
                ViewportTypes = viewportTypes,
                DefaultViewportTypeKey = defaultViewportType
            };
        }

        /// <summary>The section views behind <paramref name="ids"/> (markers or views); other elements are left out.</summary>
        public static List<long> ResolveSections(Document doc, IEnumerable<ElementId> ids)
        {
            var viewerToView = BuildViewerIndex(doc);
            var keys = new List<long>();
            foreach (ElementId id in ids ?? Array.Empty<ElementId>())
            {
                var view = doc.GetElement(id) is Element e ? ViewFor(e, viewerToView) : null;
                if (view != null && !keys.Contains(view.Id.Value))
                    keys.Add(view.Id.Value);
            }
            return keys;
        }

        /// <summary>True for a section marker (viewer element) or a section view — used to filter picking.</summary>
        public static bool IsMarkerOrSection(Element element)
            => element is ViewSection view ? IsSectionView(view)
               : element?.Category?.Id.Value == (long)BuiltInCategory.OST_Viewers;

        /// <summary>
        /// Worksharing / ACC: why the view cannot be moved — it, its viewport or its sheet is owned
        /// by someone else, or changed in central since the last reload — or null when it can.
        /// </summary>
        private static string LockReason(Document doc, View view, Viewport viewport, ViewSheet sheet)
        {
            if (!doc.IsWorkshared)
                return null;

            string reason = Lock(doc, view.Id, "the view");
            if (reason == null && viewport != null)
                reason = Lock(doc, viewport.Id, "its viewport on " + sheet?.SheetNumber);
            if (reason == null && sheet != null)
                reason = Lock(doc, sheet.Id, "sheet " + sheet.SheetNumber);
            return reason;
        }

        private static string Lock(Document doc, ElementId id, string what)
        {
            CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(doc, id, out string owner);
            if (status == CheckoutStatus.OwnedByOtherUser)
                return $"{what} is owned by {(string.IsNullOrWhiteSpace(owner) ? "another user" : owner)}";
            if (WorksharingUtils.GetModelUpdatesStatus(doc, id) == ModelUpdatesStatus.UpdatedInCentral)
                return $"{what} changed in central — Reload Latest";
            return null;
        }

        private static SheetContent ReadSheet(Document doc, ViewSheet sheet)
        {
            int schedules = new FilteredElementCollector(doc, sheet.Id)
                .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Count(s => !s.IsTitleblockRevisionSchedule);

            return new SheetContent
            {
                Key = sheet.Id.Value,
                Number = sheet.SheetNumber,
                Name = sheet.Name,
                ViewportCount = sheet.GetAllViewports().Count,
                ScheduleCount = schedules,
                LockReason = doc.IsWorkshared ? Lock(doc, sheet.Id, "sheet " + sheet.SheetNumber) : null
            };
        }

        /// <summary>Viewer element → its section view, for every section view in the model.</summary>
        private static Dictionary<ElementId, ViewSection> BuildViewerIndex(Document doc)
        {
            var index = new Dictionary<ElementId, ViewSection>();
            foreach (ViewSection view in new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>())
            {
                if (!IsSectionView(view))
                    continue;
                try
                {
                    foreach (ElementId viewerId in view.GetDependentElements(ViewerFilter))
                        index[viewerId] = view;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // A view Revit can't query is simply not offered.
                }
            }
            return index;
        }

        private static ViewSection ViewFor(Element element, Dictionary<ElementId, ViewSection> viewerToView)
        {
            if (element is ViewSection direct)
                return IsSectionView(direct) ? direct : null;
            return viewerToView.TryGetValue(element.Id, out var view) ? view : null;
        }

        private static XYZ MarkerPoint(Element viewer, View source)
        {
            try
            {
                BoundingBoxXYZ box = viewer.get_BoundingBox(source);
                return box == null ? null : (box.Min + box.Max) / 2;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        private static int SafeScale(View view)
        {
            try { return view.Scale; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return 0; }
        }

        private static (List<NamedType> Types, long Default) ReadViewportTypes(Document doc, Viewport anyViewport)
        {
            ElementId defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.ViewportType);
            ElementType seed = defaultId != ElementId.InvalidElementId ? doc.GetElement(defaultId) as ElementType : null;
            if (seed == null && anyViewport != null)
                seed = doc.GetElement(anyViewport.GetTypeId()) as ElementType;
            if (seed == null)
                return (new List<NamedType>(), 0);

            var ids = new HashSet<ElementId>(seed.GetSimilarTypes()) { seed.Id };
            var types = ids.Select(doc.GetElement).OfType<ElementType>()
                .Select(t => new NamedType(t.Id.Value, t.Name))
                .OrderBy(t => t.Name, NaturalStringComparer.Instance).ToList();
            return (types, defaultId != ElementId.InvalidElementId ? defaultId.Value : 0);
        }
    }
}
