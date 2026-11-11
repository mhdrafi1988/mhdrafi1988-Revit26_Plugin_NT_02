using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection
{
    /// <summary>
    /// Use case behind the two model panels: which models can be chosen, and how many elements of
    /// each category the chosen model has.
    /// </summary>
    public sealed class ModelSelectionService
    {
        private readonly IModelReader _reader;
        private readonly IRevitDispatcher _dispatcher;

        /// <summary>Creates the service.</summary>
        public ModelSelectionService(IModelReader reader, IRevitDispatcher dispatcher)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>The active model first, then the links by name.</summary>
        public async Task<IReadOnlyList<ModelRef>> GetModelsAsync()
        {
            IReadOnlyList<ModelRef> models = await _dispatcher.RunAsync(() => _reader.GetModels());
            return models
                .OrderByDescending(m => m.IsHost)
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>One row per structural category, in catalog order. An unloaded link gives zeros.</summary>
        public async Task<IReadOnlyList<CategoryCountDto>> GetStructuralCountsAsync(ModelRef model)
        {
            IReadOnlyDictionary<StructuralCategory, int> counts = await ReadAsync(model, () => _reader.CountStructural(model));
            return CategoryCatalog.Structural
                .Select(c => new CategoryCountDto(c.ToString(), CategoryCatalog.NameOf(c), CountOf(counts, c),
                    CategoryCatalog.CanBeCut(c) ? string.Empty : CategoryCatalog.MarkerOnlyNote))
                .ToList();
        }

        /// <summary>One row per MEP category, in catalog order. An unloaded link gives zeros.</summary>
        public async Task<IReadOnlyList<CategoryCountDto>> GetMepCountsAsync(ModelRef model)
        {
            IReadOnlyDictionary<MepCategory, int> counts = await ReadAsync(model, () => _reader.CountMep(model));
            return CategoryCatalog.Mep
                .Select(c => new CategoryCountDto(c.ToString(), CategoryCatalog.NameOf(c), CountOf(counts, c), string.Empty))
                .ToList();
        }

        private async Task<IReadOnlyDictionary<TKey, int>> ReadAsync<TKey>(ModelRef model, Func<IReadOnlyDictionary<TKey, int>> read)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (!model.IsLoaded)
                return new Dictionary<TKey, int>();
            return await _dispatcher.RunAsync(read) ?? new Dictionary<TKey, int>();
        }

        private static int CountOf<TKey>(IReadOnlyDictionary<TKey, int> counts, TKey key)
            => counts.TryGetValue(key, out int count) ? count : 0;
    }
}
