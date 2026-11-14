using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Matching;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Xunit;

namespace WetAreaWaterproofing.Tests
{
    public class KeywordMatcherTests
    {
        [Theory]
        [InlineData("Toilet 01", "toilet")]
        [InlineData("Master Bath", "bath")]
        [InlineData("WASHROOM", "wash")]
        [InlineData("W.C.", "wc")]
        [InlineData("Staff-Kitchen", "kitchen")]
        [InlineData("Bathroom_02", "bath")]
        [InlineData("Dishwasher Store", null)]
        [InlineData("Bedroom", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void MatchesWordPrefixOnly(string name, string expected)
            => Assert.Equal(expected, KeywordMatcher.Match(name, WetAreaSettings.DefaultKeywords));

        [Fact]
        public void HonoursCustomKeywords()
        {
            Assert.Equal("janitor", KeywordMatcher.Match("Janitor Closet", new[] { "janitor" }));
            Assert.Null(KeywordMatcher.Match("Toilet", new[] { "janitor" }));
        }

        [Fact]
        public void NullOrEmptyKeywordsMatchNothing()
        {
            Assert.Null(KeywordMatcher.Match("Toilet", null));
            Assert.Null(KeywordMatcher.Match("Toilet", new[] { "", null }));
        }
    }
}
