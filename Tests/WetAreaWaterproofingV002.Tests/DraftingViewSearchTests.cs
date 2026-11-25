using Revit26_Plugin_Revised.WetAreaWaterproofing.V002.Core.Sections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WetAreaWaterproofingV002.Tests
{
    public class DraftingViewSearchTests
    {
        // Sample data only.
        private static readonly List<DraftingViewInfo> Views = new()
        {
            new(1, "WP Detail - Floor Drain", "FLOOR DRAIN DETAIL", "A-501", "Wet area details", "3"),
            new(2, "WP Detail - Shower Kerb", "SHOWER KERB", "A-501", "Wet area details", "4"),
            new(3, "WP Detail - Door Threshold", "", "A-502", "Wet area details 2", "13"),
            new(4, "WP Detail - Typical Wet Area", "TYPICAL", "A-510", "Typical details", "1"),
            new(5, "WP Detail - Upturn", "", "", "", ""),
        };

        private static IList<long> Ids(string q, DraftingViewScope scope = DraftingViewScope.All)
            => DraftingViewSearch.Filter(Views, q, scope).Select(v => v.Id).ToList();

        [Fact]
        public void Empty_query_lists_every_view_placed_first_by_sheet_then_bubble()
            => Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, Ids(""));

        [Fact]
        public void Finds_by_view_name_title_sheet_number_and_sheet_name()
        {
            Assert.Equal(new long[] { 2 }, Ids("shower"));
            Assert.Equal(new long[] { 1 }, Ids("floor drain detail"));
            Assert.Equal(new long[] { 3 }, Ids("A-502"));
            Assert.Equal(new long[] { 4 }, Ids("typical details"));
        }

        [Fact]
        public void Bubble_number_alone_ranks_the_exact_bubble_first()
        {
            IList<long> hits = Ids("3");
            Assert.Equal(1, hits[0]);       // bubble 3
            Assert.Contains(3, hits);       // bubble 13 still listed, after it
        }

        [Theory]
        [InlineData("A-501/3")]
        [InlineData("A-501 / 3")]
        [InlineData("a-501/3")]
        public void Sheet_slash_bubble_finds_one_view(string q) => Assert.Equal(new long[] { 1 }, Ids(q));

        [Fact]
        public void Prefixes_narrow_a_word_to_one_field()
        {
            Assert.Equal(new long[] { 1 }, Ids("d:3"));
            Assert.Equal(new long[] { 1, 2 }, Ids("s:A-501"));
            Assert.Equal(new long[] { 2 }, Ids("t:kerb"));
            Assert.Equal(new long[] { 5 }, Ids("n:upturn"));
        }

        [Fact]
        public void Scope_filters_placed_and_unplaced_views()
        {
            Assert.Equal(new long[] { 5 }, Ids("", DraftingViewScope.NotOnSheet));
            Assert.Equal(4, Ids("", DraftingViewScope.OnSheet).Count);
        }

        [Fact]
        public void Reference_resolves_only_when_it_names_exactly_one_placed_view()
        {
            Assert.Equal(1, DraftingViewSearch.ResolveReference(Views, "A-501/3")?.Id);
            Assert.Null(DraftingViewSearch.ResolveReference(Views, "A-501/9"));
            Assert.Null(DraftingViewSearch.ResolveReference(Views, "WP Detail - Upturn"));
            Assert.Null(DraftingViewSearch.ResolveReference(Views, "New name"));
        }

        [Fact]
        public void Reference_text_that_is_a_view_name_is_left_alone()
        {
            var views = new List<DraftingViewInfo>(Views) { new(9, "A-501/3", "", "", "", "") };
            Assert.Null(DraftingViewSearch.ResolveReference(views, "A-501/3"));
        }

        [Fact]
        public void Describe_tells_placed_unplaced_and_new()
        {
            Assert.Equal("A-501 / 3 · Wet area details", DraftingViewSearch.Describe(Views, "wp detail - floor drain"));
            Assert.Equal("Not on a sheet", DraftingViewSearch.Describe(Views, "WP Detail - Upturn"));
            Assert.Equal("New view: created at the run", DraftingViewSearch.Describe(Views, "WP Detail - New"));
        }

        [Fact]
        public void Natural_order_puts_2_before_10()
            => Assert.True(NaturalComparer.Instance.Compare("A-2", "A-10") < 0);
    }
}
