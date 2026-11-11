using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Ports;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Xunit;

namespace VoidOpenings.Tests
{
    public class ModelSelectionTests
    {
        private sealed class FakeReader : IModelReader
        {
            public List<ModelRef> Models = new List<ModelRef>();
            public Dictionary<StructuralCategory, int> Structural = new Dictionary<StructuralCategory, int>();
            public Dictionary<MepCategory, int> Mep = new Dictionary<MepCategory, int>();
            public int CountCalls;

            public IReadOnlyList<ModelRef> GetModels() => Models;

            public IReadOnlyDictionary<StructuralCategory, int> CountStructural(ModelRef model) { CountCalls++; return Structural; }

            public IReadOnlyDictionary<MepCategory, int> CountMep(ModelRef model) { CountCalls++; return Mep; }

            public MepModel ReadMep(ModelRef model, IReadOnlyList<MepCategory> categories, ICollection<string> notes) => null;

            public StructuralModel ReadStructural(ModelRef model, IReadOnlyList<StructuralCategory> categories,
                Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry.Aabb? hostBoundsMm, ICollection<string> notes) => null;

            public IReadOnlyList<StoredOpening> ReadStoredOpenings(ModelRef structuralModel) => new StoredOpening[0];
        }

        private sealed class InlineDispatcher : IRevitDispatcher
        {
            public int Calls;

            public Task<T> RunAsync<T>(Func<T> work) { Calls++; return Task.FromResult(work()); }
        }

        private static ModelRef Link(string name, bool loaded = true)
            => new ModelRef("file:" + name.ToLowerInvariant(), name) { IsLoaded = loaded, LinkInstanceUniqueId = name + "-uid" };

        [Fact]
        public void Structural_categories_are_listed_in_the_order_of_the_brief()
        {
            Assert.Equal(new[]
            {
                "Floors", "Walls", "Structural Framing", "Structural Columns", "Stairs", "Landings", "Ramps",
                "Structural Foundations", "Generic Models",
            }, CategoryCatalog.Structural.Select(CategoryCatalog.NameOf).ToArray());
        }

        [Fact]
        public void Mep_categories_are_listed_in_the_order_of_the_brief()
        {
            Assert.Equal(new[]
            {
                "Pipes", "Pipe Accessories", "Pipe Fittings", "Ducts", "Duct Accessories", "Duct Fittings",
                "Cable Trays", "Cable Tray Fittings", "Conduits", "Conduit Fittings",
            }, CategoryCatalog.Mep.Select(CategoryCatalog.NameOf).ToArray());
        }

        [Fact]
        public async Task Models_are_listed_with_the_active_model_first_then_links_by_name()
        {
            var reader = new FakeReader();
            reader.Models.Add(Link("Tower_STR"));
            reader.Models.Add(new ModelRef("file:host", "Host") { IsHost = true });
            reader.Models.Add(Link("Tower_MEP"));
            var service = new ModelSelectionService(reader, new InlineDispatcher());

            var models = await service.GetModelsAsync();

            Assert.Equal(new[] { "Host", "Tower_MEP", "Tower_STR" }, models.Select(m => m.DisplayName).ToArray());
        }

        [Fact]
        public async Task Every_structural_category_gets_a_row_even_when_the_model_has_none_of_it()
        {
            var reader = new FakeReader();
            reader.Structural[StructuralCategory.Wall] = 612;
            var service = new ModelSelectionService(reader, new InlineDispatcher());

            var rows = await service.GetStructuralCountsAsync(Link("Tower_STR"));

            Assert.Equal(CategoryCatalog.Structural.Count, rows.Count);
            Assert.Equal(612, rows.Single(r => r.Name == "Walls").Count);
            Assert.Equal(0, rows.Single(r => r.Name == "Floors").Count);
            Assert.Equal("Wall", rows.Single(r => r.Name == "Walls").Key);
        }

        [Fact]
        public async Task Categories_revit_cannot_cut_carry_a_marker_only_note()
        {
            var service = new ModelSelectionService(new FakeReader(), new InlineDispatcher());

            var rows = await service.GetStructuralCountsAsync(Link("Tower_STR"));

            Assert.All(new[] { "Stairs", "Landings", "Ramps" },
                name => Assert.Equal("Marker only, no cut", rows.Single(r => r.Name == name).Note));
            Assert.Equal(string.Empty, rows.Single(r => r.Name == "Floors").Note);
        }

        [Fact]
        public async Task Mep_counts_come_back_in_catalog_order()
        {
            var reader = new FakeReader();
            reader.Mep[MepCategory.Conduit] = 2400;
            reader.Mep[MepCategory.Pipe] = 2140;
            var service = new ModelSelectionService(reader, new InlineDispatcher());

            var rows = await service.GetMepCountsAsync(Link("Tower_MEP"));

            Assert.Equal("Pipes", rows[0].Name);
            Assert.Equal(2140, rows[0].Count);
            Assert.Equal(2400, rows.Single(r => r.Key == "Conduit").Count);
        }

        [Fact]
        public async Task Model_reads_go_through_the_revit_dispatcher()
        {
            var dispatcher = new InlineDispatcher();
            var service = new ModelSelectionService(new FakeReader(), dispatcher);

            await service.GetModelsAsync();
            await service.GetMepCountsAsync(Link("Tower_MEP"));

            Assert.Equal(2, dispatcher.Calls);
        }

        [Fact]
        public async Task Unloaded_link_gives_zero_counts_without_reading_the_model()
        {
            var reader = new FakeReader();
            reader.Structural[StructuralCategory.Wall] = 5;
            var service = new ModelSelectionService(reader, new InlineDispatcher());

            var rows = await service.GetStructuralCountsAsync(Link("Tower_STR", loaded: false));

            Assert.All(rows, r => Assert.Equal(0, r.Count));
            Assert.Equal(0, reader.CountCalls);
        }
    }
}
