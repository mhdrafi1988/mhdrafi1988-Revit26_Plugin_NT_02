using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TagToText.V001.Core.Services
{
    /// <summary>
    /// Finds out why new text notes may not show in a view: Text Notes or all annotation categories
    /// switched off in Visibility/Graphics, a view template doing so, a filter that hides them, a hidden
    /// workset, or notes outside the crop region. Must be called in a Revit API context, with the notes
    /// already created (filters are tested against the real notes).
    /// </summary>
    public static class TextVisibilityChecker
    {
        /// <summary>Returns one plain-language line per reason found; empty when the notes should be visible.</summary>
        /// <param name="doc">The document.</param>
        /// <param name="view">The view the notes were created in.</param>
        /// <param name="noteIds">The new text notes.</param>
        public static List<string> Check(Document doc, View view, IReadOnlyCollection<ElementId> noteIds)
        {
            var issues = new List<string>();
            if (doc == null || view == null || noteIds == null || noteIds.Count == 0) return issues;

            string where = $"'{view.Name}'";
            string template = TemplateSuffix(doc, view);
            var notes = noteIds.Select(doc.GetElement).OfType<TextNote>().ToList();

            Safe(() =>
            {
                Category cat = Category.GetCategory(doc, BuiltInCategory.OST_TextNotes);
                if (cat != null && view.CanCategoryBeHidden(cat.Id) && view.GetCategoryHidden(cat.Id))
                    issues.Add($"The Text Notes category is switched off in Visibility/Graphics of {where}{template}.");
            });

            Safe(() =>
            {
                if (view.AreAnnotationCategoriesHidden)
                    issues.Add($"Annotation categories are hidden in {where}{template}.");
            });

            Safe(() => CheckFilters(doc, view, notes, where, template, issues));
            Safe(() => CheckWorksets(doc, view, notes, where, issues));
            Safe(() => CheckCrop(view, notes, where, issues));

            // Catch-all: Revit says hidden but none of the checks above found why.
            if (issues.Count == 0)
                Safe(() =>
                {
                    int hidden = notes.Count(n => n.IsHidden(view));
                    if (hidden > 0)
                        issues.Add($"Revit reports {hidden} new text note(s) hidden in {where}{template}. Check Visibility/Graphics, filters and temporary hide/isolate.");
                });

            return issues;
        }

        private static void CheckFilters(Document doc, View view, List<TextNote> notes, string where, string template, List<string> issues)
        {
            foreach (ElementId id in view.GetFilters())
            {
                if (!view.GetIsFilterEnabled(id) || view.GetFilterVisibility(id)) continue;
                if (doc.GetElement(id) is not FilterElement filter) continue;

                int matched = 0;
                if (filter is ParameterFilterElement pfe)
                {
                    if (!pfe.GetCategories().Contains(new ElementId(BuiltInCategory.OST_TextNotes))) continue;
                    ElementFilter rules = pfe.GetElementFilter();
                    matched = rules == null ? notes.Count : notes.Count(n => rules.PassesFilter(doc, n.Id));
                }
                else if (filter is SelectionFilterElement sfe)
                {
                    var ids = sfe.GetElementIds();
                    matched = notes.Count(n => ids.Contains(n.Id));
                }
                if (matched > 0)
                    issues.Add($"The filter '{filter.Name}' hides {matched} new text note(s) in {where}{template} (its Visibility box is off).");
            }
        }

        private static void CheckWorksets(Document doc, View view, List<TextNote> notes, string where, List<string> issues)
        {
            if (!doc.IsWorkshared) return;
            foreach (var group in notes.GroupBy(n => n.WorksetId))
            {
                if (view.GetWorksetVisibility(group.Key) != WorksetVisibility.Hidden) continue;
                string name = doc.GetWorksetTable().GetWorkset(group.Key)?.Name ?? group.Key.ToString();
                issues.Add($"Workset '{name}' (holding {group.Count()} new text note(s)) is hidden in {where}.");
            }
        }

        private static void CheckCrop(View view, List<TextNote> notes, string where, List<string> issues)
        {
            if (!view.CropBoxActive) return;
            BoundingBoxXYZ crop = view.CropBox;
            Transform toView = crop.Transform.Inverse;
            int outside = 0;
            foreach (TextNote n in notes)
            {
                BoundingBoxXYZ bb = n.get_BoundingBox(view);
                if (bb == null) continue;
                XYZ p = toView.OfPoint((bb.Min + bb.Max) / 2);
                if (p.X < crop.Min.X || p.X > crop.Max.X || p.Y < crop.Min.Y || p.Y > crop.Max.Y) outside++;
            }
            if (outside > 0)
                issues.Add($"{outside} new text note(s) are outside the crop region of {where}, so they are not shown.");
        }

        private static string TemplateSuffix(Document doc, View view)
        {
            if (view.ViewTemplateId == ElementId.InvalidElementId) return string.Empty;
            string name = doc.GetElement(view.ViewTemplateId)?.Name;
            return name == null ? string.Empty : $" (set by view template '{name}')";
        }

        // A failing check must never stop the run; it is simply not reported.
        private static void Safe(Action check)
        {
            try { check(); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
        }
    }
}
