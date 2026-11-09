using Revit26_Plugin_Revised.Shared.Services;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V006
{
    /// <summary>
    /// Scans the currently-checked section view(s) for model categories with
    /// visible elements, and combines with TagFamilyResolverService to flag
    /// taggable vs. untaggable and populate available tag types. Produces
    /// the CategoryTagRow list shown in the "Categories in Selected View(s)"
    /// checklist.
    ///
    /// ASSUMPTION (per file-list note): categories are scanned across the
    /// UNION of all checked views, not per-view — matches the single shared
    /// checklist in the mockup.
    ///
    /// V003: uses ResolveAllTagTypes (not just the first match) so each row
    /// can offer a full Tag Type dropdown when 2+ families are loaded.
    /// </summary>
    public class CategoryScanService
    {
        private readonly TagFamilyResolverService _resolver = new();

        public List<CategoryTagRow> ScanCategories(Document doc, IEnumerable<ElementId> viewIds)
        {
            var foundCategories = new HashSet<BuiltInCategory>();

            foreach (var viewId in viewIds)
            {
                ToolCancellation.CheckCurrent();
                var collector = new FilteredElementCollector(doc, viewId)
                    .WhereElementIsNotElementType();

                foreach (var el in collector)
                {
                    var cat = el.Category;
                    if (cat == null) continue;
                    if (cat.CategoryType != CategoryType.Model) continue;

                    // BuiltInCategory enum values are always negative in the
                    // Revit API. A positive Category.Id means this is a
                    // user/family-defined category with no BuiltInCategory
                    // equivalent — casting it anyway (the original bug) produced
                    // a garbage enum value that silently misbehaved downstream
                    // instead of throwing. Skip those; this tool only supports
                    // categories with a real BuiltInCategory mapping.
                    long idValue = cat.Id.Value;
                    if (idValue >= 0 || idValue < int.MinValue) continue;

                    var bic = (BuiltInCategory)idValue;
                    if (!System.Enum.IsDefined(typeof(BuiltInCategory), bic)) continue;

                    foundCategories.Add(bic);
                }
            }

            var rows = new List<CategoryTagRow>();

            foreach (var bic in foundCategories)
            {
                var catObj = Category.GetCategory(doc, bic);
                string name = catObj?.Name ?? bic.ToString();

                var tagTypes = _resolver.ResolveAllTagTypes(doc, bic, out string reason);
                bool isTaggable = tagTypes.Count > 0;

                rows.Add(new CategoryTagRow(bic, name, isTaggable, tagTypes, isTaggable ? string.Empty : reason));
            }

            return rows.OrderBy(r => r.CategoryName).ToList();
        }
    }
}
