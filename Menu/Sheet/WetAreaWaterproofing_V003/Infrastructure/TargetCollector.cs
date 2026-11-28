using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Mapping;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Infrastructure
{
    /// <summary>One model × category the Mapping reads, with how many mapped elements it holds.</summary>
    /// <param name="Model">"Host" or a link's name.</param>
    /// <param name="Category">Category name.</param>
    /// <param name="Count">Elements of that category that match at least one Mapping row.</param>
    /// <param name="On">Read; off when the model is unticked under "Look in".</param>
    public sealed record TargetSource(string Model, string Category, int Count, bool On)
    {
        /// <summary>"Model|Category".</summary>
        public string Key => Model + "|" + Category;
    }

    /// <summary>
    /// Reads the elements of the Mapping's categories from the host model and its links, in host coordinates
    /// (tool 3.0.0). Only the categories under "Categories to load" are read, and only from the ticked models.
    /// </summary>
    public static class TargetCollector
    {
        /// <summary>The models the Mapping reads: the host (when ticked) and every loaded link not unticked.</summary>
        public static List<(Document Doc, Transform ToHost, string Model)> Sources(Document host, IEnumerable<RevitLinkInstance> links, WetAreaSettings s)
        {
            var result = new List<(Document, Transform, string)>();
            if (s.MappingHost) result.Add((host, Transform.Identity, RoomCollector.HostKey));
            var off = new HashSet<string>(s.MappingLinksOff ?? new List<string>());
            foreach (RevitLinkInstance link in links ?? Enumerable.Empty<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                string name = RoomCollector.LinkName(link);
                if (linkDoc != null && !off.Contains(name)) result.Add((linkDoc, link.GetTotalTransform(), name));
            }
            return result;
        }

        /// <summary>The category of <paramref name="doc"/> called <paramref name="name"/>, or null.</summary>
        public static Category FindCategory(Document doc, string name)
        {
            foreach (Category c in doc.Settings.Categories)
                if (c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>"Family : Type" of <paramref name="element"/>, or its type name.</summary>
        public static string TypeName(Document doc, Element element)
        {
            if (doc.GetElement(element.GetTypeId()) is not ElementType type) return string.Empty;
            return string.IsNullOrEmpty(type.FamilyName) ? type.Name : type.FamilyName + " : " + type.Name;
        }

        /// <summary>
        /// Reads every loaded category of the ticked models. An element matched by several rows comes back once
        /// per row (each item carries its row's id).
        /// </summary>
        public static (List<FoundItem> Items, List<TargetSource> Sources) Collect(
            Document host, IEnumerable<RevitLinkInstance> links, WetAreaSettings s)
        {
            var items = new List<FoundItem>();
            var sources = new List<TargetSource>();
            var rules = (s.Mappings ?? new List<MappingRule>()).Where(r => r.Category != MappingRules.RoomCategory).ToList();
            var categories = (s.MappingCategories ?? new List<string>())
                .Where(c => rules.Any(r => string.Equals(r.Category, c, StringComparison.OrdinalIgnoreCase))).ToList();

            foreach ((Document doc, Transform toHost, string model) in Sources(host, links, s))
                foreach (string name in categories)
                {
                    ToolCancellation.CheckCurrent();
                    Category category = FindCategory(doc, name);
                    if (category == null) continue;
                    int count = 0;
                    foreach (Element element in new FilteredElementCollector(doc).OfCategoryId(category.Id).WhereElementIsNotElementType())
                    {
                        string typeName = TypeName(doc, element);
                        var matches = ItemClassifier.Classify(category.Name, typeName, rules).ToList();
                        if (matches.Count == 0) continue;
                        FoundItem item = Read(element, toHost, model, category.Name, typeName);
                        if (item == null) continue;
                        count++;
                        foreach ((MappingRule rule, SectionTargetKind kind) in matches)
                            items.Add(item with { Kind = kind, RuleId = rule.Id });
                    }
                    sources.Add(new TargetSource(model, category.Name, count, true));
                }

            // Two instances of one link share a name: show one row per model and category.
            sources = sources.GroupBy(x => x.Key)
                .Select(g => new TargetSource(g.First().Model, g.First().Category, g.Sum(x => x.Count), true)).ToList();
            return (items, sources);
        }

        /// <summary>The element's position (point, or location line for walls), in host coordinates; null when it has none.</summary>
        private static FoundItem Read(Element element, Transform toHost, string model, string category, string typeName)
        {
            if (element.Location is LocationCurve lc && lc.Curve is Curve curve && curve.IsBound)
            {
                XYZ a = toHost.OfPoint(curve.GetEndPoint(0)), b = toHost.OfPoint(curve.GetEndPoint(1));
                XYZ mid = (a + b) / 2;
                double half = element is Wall wall ? wall.Width / 2 * BoundaryReader.FtToMm : 0;
                double z = Math.Min(a.Z, b.Z);
                if (element is Wall w && w.Document.GetElement(w.LevelId) is Level level)
                    z = toHost.OfPoint(new XYZ(0, 0, Elevations.InternalZFt(level))).Z
                        + (w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0);
                return new FoundItem(SectionTargetKind.Element, BoundaryReader.ToPt(mid), z * BoundaryReader.FtToMm,
                    element.Id.Value, model, category, typeName)
                {
                    IsLine = true, LineStart = BoundaryReader.ToPt(a), LineEnd = BoundaryReader.ToPt(b), HalfWidthMm = half
                };
            }

            XYZ at = (element.Location as LocationPoint)?.Point;
            if (at == null)
            {
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null) return null;
                at = (box.Min + box.Max) / 2;
            }
            XYZ p = toHost.OfPoint(at);
            return new FoundItem(SectionTargetKind.Element, BoundaryReader.ToPt(p), p.Z * BoundaryReader.FtToMm,
                element.Id.Value, model, category, typeName);
        }

        /// <summary>
        /// Categories with at least one placed element in the ticked models, for the "+ Add category" search.
        /// Only model categories are offered (no annotation, analytical or internal ones).
        /// </summary>
        public static List<CategoryChoice> ListCategories(Document host, IEnumerable<RevitLinkInstance> links, WetAreaSettings s)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach ((Document doc, _, _) in Sources(host, links, s))
                foreach (Category c in doc.Settings.Categories)
                {
                    ToolCancellation.CheckCurrent();
                    if (c == null || c.CategoryType != CategoryType.Model || c.IsTagCategory || !c.AllowsBoundParameters) continue;
                    if (new FilteredElementCollector(doc).OfCategoryId(c.Id).WhereElementIsNotElementType().FirstElementId() == ElementId.InvalidElementId) continue;
                    counts[c.Name] = counts.TryGetValue(c.Name, out int n) ? n + 1 : 1;
                }
            return counts.Select(kv => new CategoryChoice(kv.Key, kv.Value)).OrderBy(c => c.Name).ToList();
        }

        /// <summary>Every "Family : Type" of the loaded categories in the ticked models, with how many are placed.</summary>
        public static List<LoadedType> LoadTypes(Document host, IEnumerable<RevitLinkInstance> links, WetAreaSettings s)
        {
            var result = new List<LoadedType>();
            foreach ((Document doc, _, string model) in Sources(host, links, s))
                foreach (string name in s.MappingCategories ?? new List<string>())
                {
                    ToolCancellation.CheckCurrent();
                    Category category = FindCategory(doc, name);
                    if (category == null) continue;
                    result.AddRange(new FilteredElementCollector(doc).OfCategoryId(category.Id).WhereElementIsNotElementType()
                        .GroupBy(e => TypeName(doc, e))
                        .Select(g => new LoadedType(category.Name, g.Key, model, g.Count())));
                }
            return result.GroupBy(t => (t.Category, t.TypeName, t.Model))
                .Select(g => new LoadedType(g.Key.Category, g.Key.TypeName, g.Key.Model, g.Sum(t => t.Count)))
                .OrderBy(t => t.Category).ThenBy(t => t.TypeName).ToList();
        }
    }
}
