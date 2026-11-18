using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary>
    /// <see cref="IGeometryReader"/> over the active Revit project. Call only in the Revit API
    /// context. One element that fails to read never stops the batch: it is listed as not analysed.
    /// </summary>
    public sealed class RevitGeometryReader : IGeometryReader
    {
        private static readonly Dictionary<BuiltInCategory, MepCategory> MepByRevitCategory = BuildMap(CategoryCatalog.Mep, CategoryMap.Of);
        private static readonly Dictionary<BuiltInCategory, StructuralCategory> StructuralByRevitCategory = BuildMap(CategoryCatalog.Structural, CategoryMap.Of);

        private readonly RevitContext _context;

        /// <summary>Creates the reader.</summary>
        public RevitGeometryReader(RevitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IReadOnlyList<long> ListMep(ModelRef model, IReadOnlyList<MepCategory> categories)
        {
            ModelLocation location = Locate(model);
            return Collect(location.Document, categories.SelectMany(CategoryMap.Of), null);
        }

        /// <inheritdoc/>
        public GeometryBatch<MepElementData> ReadMep(ModelRef model, IReadOnlyList<long> elementIds)
        {
            ModelLocation location = Locate(model);
            return Read(location, elementIds, MepByRevitCategory, CategoryCatalog.NameOf,
                (Element element, MepCategory category, out string reason) => MepExtractor.TryRead(element, category, location.ToHost, out reason));
        }

        /// <inheritdoc/>
        public IReadOnlyList<long> ListStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories, Aabb? regionMm)
        {
            ModelLocation location = Locate(model);
            Outline outline = regionMm.HasValue ? OutlineIn(location, regionMm.Value) : null;
            return Collect(location.Document, categories.SelectMany(CategoryMap.Of), outline);
        }

        /// <inheritdoc/>
        public GeometryBatch<StructuralElementData> ReadStructural(ModelRef model, IReadOnlyList<long> elementIds)
        {
            ModelLocation location = Locate(model);
            return Read(location, elementIds, StructuralByRevitCategory, CategoryCatalog.NameOf,
                (Element element, StructuralCategory category, out string reason) => StructuralExtractor.TryRead(element, category, location.ToHost, out reason));
        }

        private delegate TData Extract<TData, TCategory>(Element element, TCategory category, out string reason);

        private ModelLocation Locate(ModelRef model) => LinkCatalog.Locate(_context.RequireProject(), model);

        private static IReadOnlyList<long> Collect(Document document, IEnumerable<BuiltInCategory> categories, Outline outline)
        {
            var revitCategories = categories.Distinct().ToList();
            if (revitCategories.Count == 0)
                return new List<long>();

            FilteredElementCollector collector = new FilteredElementCollector(document)
                .WherePasses(new ElementMulticategoryFilter(revitCategories))
                .WhereElementIsNotElementType();
            if (outline != null)
                collector = collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
            return collector.ToElementIds().Select(id => id.Value).ToList();
        }

        private static GeometryBatch<TData> Read<TData, TCategory>(ModelLocation location, IReadOnlyList<long> elementIds,
            Dictionary<BuiltInCategory, TCategory> categoryOf, Func<TCategory, string> nameOf, Extract<TData, TCategory> extract)
            where TData : class
        {
            var elements = new List<TData>();
            var skipped = new List<SkippedElement>();

            foreach (long id in elementIds ?? new List<long>())
            {
                Element element = location.Document.GetElement(new ElementId(id));
                if (element == null || !element.IsValidObject)
                {
                    skipped.Add(new SkippedElement(location.Name, id, string.Empty, "It is no longer in the model."));
                    continue;
                }

                string categoryName = element.Category?.Name ?? string.Empty;
                if (element.Category == null || !categoryOf.TryGetValue(element.Category.BuiltInCategory, out TCategory category))
                {
                    skipped.Add(new SkippedElement(location.Name, id, categoryName, "Its category is not one the tool analyses."));
                    continue;
                }

                try
                {
                    TData data = extract(element, category, out string reason);
                    if (data != null) elements.Add(data);
                    else skipped.Add(new SkippedElement(location.Name, id, nameOf(category), reason));
                }
                catch (Exception ex)
                {
                    // One odd element must not cost the user the whole run.
                    skipped.Add(new SkippedElement(location.Name, id, nameOf(category), "Its geometry could not be read: " + ex.Message));
                }
            }

            return new GeometryBatch<TData>(elements, skipped);
        }

        /// <summary>The region, given in host millimetres, as an outline in the model's own coordinates.</summary>
        private static Outline OutlineIn(ModelLocation location, Aabb regionMm)
        {
            Transform toModel = location.ToHost.Inverse;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            // A rotated link turns the box, so all eight corners are carried over and boxed again.
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vec3(
                    (corner & 1) == 0 ? regionMm.Min.X : regionMm.Max.X,
                    (corner & 2) == 0 ? regionMm.Min.Y : regionMm.Max.Y,
                    (corner & 4) == 0 ? regionMm.Min.Z : regionMm.Max.Z);
                XYZ p = toModel.OfPoint(UnitsMm.ToXyz(point));
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        private static Dictionary<BuiltInCategory, TCategory> BuildMap<TCategory>(IReadOnlyList<TCategory> categories,
            Func<TCategory, IReadOnlyList<BuiltInCategory>> revitCategoriesOf)
        {
            var map = new Dictionary<BuiltInCategory, TCategory>();
            foreach (TCategory category in categories)
                foreach (BuiltInCategory revitCategory in revitCategoriesOf(category))
                    map[revitCategory] = category;
            return map;
        }
    }
}
