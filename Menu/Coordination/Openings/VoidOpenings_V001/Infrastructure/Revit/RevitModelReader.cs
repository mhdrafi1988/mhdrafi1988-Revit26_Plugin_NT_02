using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Revit
{
    /// <summary><see cref="IModelReader"/> over the active Revit project. Call only in the Revit API context.</summary>
    public sealed class RevitModelReader : IModelReader
    {
        private readonly RevitContext _context;

        /// <summary>Creates the reader.</summary>
        public RevitModelReader(RevitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public IReadOnlyList<ModelRef> GetModels() => LinkCatalog.GetModels(_context.RequireProject());

        /// <inheritdoc/>
        public IReadOnlyDictionary<StructuralCategory, int> CountStructural(ModelRef model)
        {
            var counts = new Dictionary<StructuralCategory, int>();
            Document document = DocumentOf(model);
            if (document == null)
                return counts;
            foreach (StructuralCategory category in CategoryCatalog.Structural)
                counts[category] = Count(document, CategoryMap.Of(category));
            return counts;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<MepCategory, int> CountMep(ModelRef model)
        {
            var counts = new Dictionary<MepCategory, int>();
            Document document = DocumentOf(model);
            if (document == null)
                return counts;
            foreach (MepCategory category in CategoryCatalog.Mep)
                counts[category] = Count(document, CategoryMap.Of(category));
            return counts;
        }

        /// <inheritdoc/>
        public MepModel ReadMep(ModelRef model, IReadOnlyList<MepCategory> categories, ICollection<string> notes)
        {
            Document host = _context.RequireProject();
            Document document = RequireDocument(host, model);
            return new MepModel(model, MepExtractor.Read(document, LinkCatalog.TransformOf(host, model), categories, notes));
        }

        /// <inheritdoc/>
        public StructuralModel ReadStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories,
            Aabb? hostBoundsMm, ICollection<string> notes)
        {
            Document host = _context.RequireProject();
            Document document = RequireDocument(host, model);
            Transform toHost = LinkCatalog.TransformOf(host, model);
            Outline outline = hostBoundsMm.HasValue ? UnitsMm.ToOutline(hostBoundsMm.Value, toHost) : null;
            return new StructuralModel(model, StructuralExtractor.Read(document, toHost, categories, outline, notes))
            {
                ToHost = UnitsMm.ToTransform3(toHost),
            };
        }

        /// <inheritdoc/>
        public IReadOnlyList<StoredOpening> ReadStoredOpenings(ModelRef structuralModel)
        {
            Document document = DocumentOf(structuralModel);
            return document == null ? new List<StoredOpening>() : OpeningStoreEs.ReadAll(document);
        }

        private static Document RequireDocument(Document host, ModelRef model)
            => LinkCatalog.Resolve(host, model)
               ?? throw new InvalidOperationException($"{model.DisplayName} is not loaded any more. Press Refresh and choose the model again.");

        /// <summary>The model's document, or null when its link was removed or unloaded since the list was read.</summary>
        private Document DocumentOf(ModelRef model) => LinkCatalog.Resolve(_context.RequireProject(), model);

        private static int Count(Document document, IReadOnlyList<BuiltInCategory> categories)
        {
            var filter = new ElementMulticategoryFilter(new List<BuiltInCategory>(categories));
            return new FilteredElementCollector(document)
                .WherePasses(filter)
                .WhereElementIsNotElementType()
                .GetElementCount();
        }
    }
}
