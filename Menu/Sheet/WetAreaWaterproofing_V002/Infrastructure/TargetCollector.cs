using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Infrastructure
{
    /// <summary>One row of the "Search targets in" list.</summary>
    /// <param name="Model">"Host" or a link's name.</param>
    /// <param name="Category">Doors, Plumbing Fixtures or Pipe Accessories.</param>
    /// <param name="Count">Elements of that category that are a door, drain, WC or shower.</param>
    /// <param name="On">Searched; off when the user unticked it.</param>
    public sealed record TargetSource(string Model, string Category, int Count, bool On)
    {
        /// <summary>Key used in the settings' unticked list.</summary>
        public string Key => Model + "|" + Category;
    }

    /// <summary>Finds the doors, drains and fixtures of the host model and its links, in host coordinates.</summary>
    public static class TargetCollector
    {
        private static readonly (string Name, BuiltInCategory Category)[] Searched =
        {
            (ItemClassifier.Doors, BuiltInCategory.OST_Doors),
            (ItemClassifier.PlumbingFixtures, BuiltInCategory.OST_PlumbingFixtures),
            (ItemClassifier.PipeAccessories, BuiltInCategory.OST_PipeAccessory)
        };

        /// <summary>
        /// Reads every searched category of the host and of <paramref name="links"/>. Items of
        /// unticked sources are counted for the list but not returned.
        /// </summary>
        public static (List<FoundItem> Items, List<TargetSource> Sources) Collect(
            Document host, IEnumerable<RevitLinkInstance> links, WetAreaSettings s)
        {
            var items = new List<FoundItem>();
            var sources = new List<TargetSource>();
            var off = new HashSet<string>(s.SourcesOff ?? new List<string>());

            Read(host, Transform.Identity, RoomCollector.HostKey, s, off, items, sources);
            foreach (RevitLinkInstance link in links ?? Enumerable.Empty<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc != null) Read(linkDoc, link.GetTotalTransform(), RoomCollector.LinkName(link), s, off, items, sources);
            }

            // Two instances of one link share a name: show one row per model and category.
            sources = sources.GroupBy(x => x.Key)
                .Select(g => new TargetSource(g.First().Model, g.First().Category, g.Sum(x => x.Count), g.First().On)).ToList();
            return (items, sources);
        }

        private static void Read(Document doc, Transform toHost, string model, WetAreaSettings s,
            HashSet<string> off, List<FoundItem> items, List<TargetSource> sources)
        {
            foreach ((string name, BuiltInCategory category) in Searched)
            {
                ToolCancellation.CheckCurrent();
                bool on = !off.Contains(model + "|" + name);
                int count = 0;
                foreach (Element element in new FilteredElementCollector(doc).OfCategory(category).WhereElementIsNotElementType())
                {
                    XYZ at = (element.Location as LocationPoint)?.Point;
                    if (at == null)
                    {
                        BoundingBoxXYZ box = element.get_BoundingBox(null);
                        if (box == null) continue;
                        at = (box.Min + box.Max) / 2;
                    }

                    string typeName = string.Empty;
                    if (doc.GetElement(element.GetTypeId()) is ElementType type)
                        typeName = string.IsNullOrEmpty(type.FamilyName) ? type.Name : type.FamilyName + " : " + type.Name;

                    SectionTargetKind? kind = ItemClassifier.Classify(name, typeName, s);
                    if (kind == null) continue;
                    count++;
                    if (!on) continue;

                    XYZ p = toHost.OfPoint(at);
                    items.Add(new FoundItem(kind.Value, BoundaryReader.ToPt(p), p.Z * BoundaryReader.FtToMm,
                        element.Id.Value, model, name, typeName));
                }
                sources.Add(new TargetSource(model, name, count, on));
            }
        }
    }
}
