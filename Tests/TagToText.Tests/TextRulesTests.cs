using System.Text.Json;
using Revit26_Plugin_Revised.TagToText.V001.Core.Models;
using Revit26_Plugin_Revised.TagToText.V001.Core.Services;
using Xunit;

namespace TagToText.Tests
{
    public class TextRulesTests
    {
        [Fact]
        public void SplitLines_handles_every_line_break_and_drops_blanks()
        {
            Assert.Equal(new[] { "N 2504.120", "E 1180.440" }, TextRules.SplitLines("N 2504.120\r\nE 1180.440"));
            Assert.Equal(new[] { "A", "B", "C" }, TextRules.SplitLines(" A \rB\n\nC "));
            Assert.Empty(TextRules.SplitLines("   "));
            Assert.Empty(TextRules.SplitLines(null));
        }

        [Fact]
        public void PrefixOnce_adds_missing_prefix_only()
        {
            Assert.Equal("TOS +12.450", TextRules.PrefixOnce("TOS", "+12.450"));
            Assert.Equal("TOS +12.450", TextRules.PrefixOnce("TOS", "TOS +12.450"));
            Assert.Equal("+12.450", TextRules.PrefixOnce(null, "+12.450"));
            Assert.Equal("N 10.000", TextRules.PrefixOnce("n", "N 10.000"));
        }

        [Fact]
        public void SuffixOnce_adds_missing_suffix_only()
        {
            Assert.Equal("12.450 FFL", TextRules.SuffixOnce("12.450", "FFL"));
            Assert.Equal("12.450 FFL", TextRules.SuffixOnce("12.450 FFL", "FFL"));
            Assert.Equal("12.450", TextRules.SuffixOnce("12.450", ""));
        }

        [Fact]
        public void ApplyUserAffixes_adds_text_exactly_as_typed()
        {
            Assert.Equal(new[] { "EL +12.450 m" }, TextRules.ApplyUserAffixes(new[] { "+12.450" }, "EL ", " m"));
            Assert.Equal(new[] { "x", "y" }, TextRules.ApplyUserAffixes(new[] { "x", "y" }, null, null));
        }

        [Fact]
        public void UniqueViewName_numbers_taken_names_and_cleans_bad_characters()
        {
            Assert.Equal("Roof - Text", TextRules.UniqueViewName("Roof - Text", new[] { "Roof" }));
            Assert.Equal("Roof - Text (2)", TextRules.UniqueViewName("Roof - Text", new[] { "roof - text" }));
            Assert.Equal("Roof - Text (3)", TextRules.UniqueViewName("Roof - Text", new[] { "Roof - Text", "Roof - Text (2)" }));
            Assert.Equal("L1- Plan", TextRules.UniqueViewName("L1: Plan", new string[0]));
            Assert.Equal("Tag To Text", TextRules.UniqueViewName("  ", null));
        }

        [Fact]
        public void Settings_defaults_match_the_old_behaviour_and_round_trip()
        {
            var s = new TagToTextSettings();
            Assert.Equal(TargetMode.SameView, s.Target);
            Assert.Equal(OriginalTagAction.Keep, s.OriginalAction);
            Assert.Equal(DuplicateMode.WithoutDetailing, s.Duplicate);
            Assert.True(s.KeepLeader);

            s.Target = TargetMode.NewView;
            s.Duplicate = DuplicateMode.WithDetailing;
            string json = JsonSerializer.Serialize(s);
            var back = JsonSerializer.Deserialize<TagToTextSettings>(json).Migrate();
            Assert.Equal(TargetMode.NewView, back.Target);
            Assert.Equal(DuplicateMode.WithDetailing, back.Duplicate);
            Assert.Equal(TagToTextSettings.CurrentVersion, back.Version);
        }

        [Fact]
        public void Settings_file_without_version_or_strings_is_migrated()
        {
            var s = JsonSerializer.Deserialize<TagToTextSettings>("{\"Prefix\":null,\"NewViewSuffix\":null,\"Version\":0}").Migrate();
            Assert.Equal(TagToTextSettings.CurrentVersion, s.Version);
            Assert.Equal(string.Empty, s.Prefix);
            Assert.Equal(" - Text", s.NewViewSuffix);
        }

        [Fact]
        public void Version_1_settings_file_gets_group_by_type_on_and_the_choice_round_trips()
        {
            var v1 = JsonSerializer.Deserialize<TagToTextSettings>("{\"Version\":1,\"Prefix\":\"EL \"}").Migrate();
            Assert.Equal(2, v1.Version);
            Assert.True(v1.GroupByType);
            Assert.Equal("EL ", v1.Prefix);

            v1.GroupByType = false;
            var back = JsonSerializer.Deserialize<TagToTextSettings>(JsonSerializer.Serialize(v1)).Migrate();
            Assert.False(back.GroupByType);
        }
    }
}
