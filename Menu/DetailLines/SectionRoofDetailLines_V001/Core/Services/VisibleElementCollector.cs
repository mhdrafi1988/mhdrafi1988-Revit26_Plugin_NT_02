using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Services
{
    /// <summary>
    /// Lists what a section view shows, from the host model and from every loaded Revit link.
    /// Read-only — nothing here opens a transaction.
    /// <para>
    /// The host model is read with a view-bound <see cref="FilteredElementCollector"/>, which is
    /// Revit's own answer to "what is visible here": it already applies the crop, the far clip,
    /// visibility/graphics, view filters and anything hidden by hand.
    /// </para>
    /// <para>
    /// Revit offers no such collector for a linked document, so a link is read instead by
    /// geometry: the section's visible box is brought into the link's coordinates and each
    /// element's bounding box tested against it (<see cref="SectionFrame.MayIntersect"/>). That
    /// catches the crop and the far clip, which is what decides whether a roof or wall is cut at
    /// all, but it cannot read a category hidden inside the link or a per-link visibility
    /// override. The test is deliberately generous there: an element the view does not in fact
    /// show may be listed, and can be unticked, whereas a stricter test would silently drop a
    /// roof the user can see.
    /// </para>
    /// </summary>
    public sealed class VisibleElementCollector
    {
        private const string HostLabel = "Host model";

        /// <summary>Pad, in feet, on the visible-box test for linked elements — about 10 mm, so an
        /// element that only just touches the box is not lost to rounding.</summary>
        private const double BoxPadFeet = 0.0328;

        /// <summary>
        /// Every element <paramref name="frame"/>'s view shows, as one row per element.
        /// <paramref name="notes"/> collects anything that could not be read.
        /// </summary>
        public List<VisibleElementRow> Collect(Document doc, SectionFrame frame, List<LogEntry> notes)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            var rows = new List<VisibleElementRow>();
            ReadHost(doc, frame, rows);
            ReadLinks(doc, frame, rows, notes);
            return rows;
        }

        private void ReadHost(Document doc, SectionFrame frame, List<VisibleElementRow> rows)
        {
            IEnumerable<Element> elements;
            try
            {
                elements = new FilteredElementCollector(doc, frame.View.Id)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }
            catch (Exception)
            {
                // A view Revit will not accept for a view-bound collector (an argument error as
                // much as an application one): nothing of the host model is listed for it, and
                // the section is reported as showing nothing rather than the run failing.
                return;
            }

            foreach (Element element in elements)
            {
                VisibleElementRow row = Describe(doc, element, 0, HostLabel, ElementSourceKind.Host, frame);
                if (row != null) rows.Add(row);
            }
        }

        private void ReadLinks(Document doc, SectionFrame frame, List<VisibleElementRow> rows, List<LogEntry> notes)
        {
            List<RevitLinkInstance> links;
            try
            {
                // Only links the view itself shows: a link hidden in this view, or one whose
                // category is switched off, never reaches this list.
                links = new FilteredElementCollector(doc, frame.View.Id)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();
            }
            catch (Exception)
            {
                return;
            }

            foreach (RevitLinkInstance link in links)
            {
                Document linkDoc;
                Transform linkToModel;
                try
                {
                    linkDoc = link.GetLinkDocument();
                    linkToModel = link.GetTotalTransform();
                }
                catch (Exception ex)
                {
                    notes?.Add(new LogEntry(LogLevel.Warning,
                        $"Link '{SafeName(link)}' could not be read ({ex.Message}) — its elements are not listed."));
                    continue;
                }

                if (linkDoc == null)
                {
                    notes?.Add(new LogEntry(LogLevel.Info,
                        $"Link '{SafeName(link)}' is not loaded — its elements are not listed."));
                    continue;
                }

                string label = SafeName(link);
                long linkId = link.Id.Value;
                int found = 0;

                try
                {
                    Outline outline = frame.OutlineInLinkSpace(linkToModel, BoxPadFeet);
                    var inBox = new BoundingBoxIntersectsFilter(outline);

                    foreach (Element element in new FilteredElementCollector(linkDoc)
                                 .WhereElementIsNotElementType()
                                 .WherePasses(inBox)
                                 .ToElements())
                    {
                        if (!IsInView(element, frame, linkToModel)) continue;
                        VisibleElementRow row = Describe(linkDoc, element, linkId, label, ElementSourceKind.Link, frame);
                        if (row == null) continue;
                        rows.Add(row);
                        found++;
                    }
                }
                catch (Exception ex)
                {
                    notes?.Add(new LogEntry(LogLevel.Warning,
                        $"Link '{label}' could not be searched ({ex.Message}) — its elements are not listed."));
                    continue;
                }

                if (found == 0)
                    notes?.Add(new LogEntry(LogLevel.Info,
                        $"Link '{label}': nothing of it reaches section '{frame.View.Name}'."));
            }
        }

        private static bool IsInView(Element element, SectionFrame frame, Transform linkToModel)
        {
            BoundingBoxXYZ box;
            try
            {
                box = element.get_BoundingBox(null);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
            return box != null && frame.MayIntersect(box, linkToModel, BoxPadFeet);
        }

        private static VisibleElementRow Describe(
            Document owningDoc, Element element, long linkInstanceId,
            string sourceLabel, ElementSourceKind source, SectionFrame frame)
        {
            if (element?.Category == null) return null;

            ElementId typeId;
            try
            {
                typeId = element.GetTypeId();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                typeId = ElementId.InvalidElementId;
            }

            Element typeElement = typeId == ElementId.InvalidElementId ? null : owningDoc.GetElement(typeId);
            string categoryName = element.Category.Name ?? "(no category)";

            return new VisibleElementRow
            {
                Key = new ElementKey(linkInstanceId, element.Id.Value),
                Type = new TypeKey(linkInstanceId, typeId == ElementId.InvalidElementId ? 0 : typeId.Value),
                SourceLabel = sourceLabel,
                Source = source,
                CategoryName = categoryName,
                FamilyName = FamilyNameOf(typeElement, categoryName),
                TypeName = typeElement?.Name ?? SafeTypeName(element),
                Role = RoleOf(element.Category),
                ViewKey = frame.View.Id.Value,
                ViewName = frame.View.Name
            };
        }

        /// <summary>
        /// What the tool can do with an element of this category: trace a roof's cut profile, put a
        /// line on a wall, or list it and nothing more.
        /// </summary>
        public static ElementRole RoleOf(Category category)
        {
            if (category?.Id == null) return ElementRole.Listed;
            return (BuiltInCategory)category.Id.Value switch
            {
                BuiltInCategory.OST_Roofs => ElementRole.Roof,
                BuiltInCategory.OST_Walls => ElementRole.Wall,
                _ => ElementRole.Listed
            };
        }

        // A loadable family's type carries its family name; a system family's does not, so its
        // types group under the category name — the way Revit's own Project Browser shows them.
        private static string FamilyNameOf(Element typeElement, string categoryName)
        {
            if (typeElement == null) return categoryName;
            try
            {
                string name = typeElement.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString();
                return string.IsNullOrWhiteSpace(name) ? categoryName : name;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return categoryName;
            }
        }

        private static string SafeTypeName(Element element)
        {
            try { return element.Name ?? "(unnamed)"; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return "(unnamed)"; }
        }

        private static string SafeName(RevitLinkInstance link)
        {
            try { return string.IsNullOrWhiteSpace(link.Name) ? $"link {link.Id.Value}" : link.Name; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return $"link {link.Id.Value}"; }
        }
    }
}
