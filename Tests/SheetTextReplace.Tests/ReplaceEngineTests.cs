using System.Linq;
using System.Text.RegularExpressions;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services;
using Xunit;

namespace SheetTextReplace.Tests
{
    public class ReplaceEngineTests
    {
        private static ReplaceOutcome Run(string text, ReplaceOptions o)
        {
            var outcome = ReplaceEngine.Run(text, o);
            // The edit list must always rebuild exactly the new text: that is what Revit applies.
            Assert.Equal(outcome.NewText, ReplaceEngine.ApplyEdits(text, outcome.EditPasses));
            Assert.Equal(outcome.OldText, string.Concat(outcome.OldSegments.Select(s => s.Text)));
            Assert.Equal(outcome.NewText, string.Concat(outcome.NewSegments.Select(s => s.Text)));
            return outcome;
        }

        private static ReplaceOptions Typ(bool trim = false) => new() { Find = "TYP.", Replacement = "TYPICAL", TrimSpaces = trim };

        [Fact]
        public void Plain_find_is_case_insensitive_by_default()
        {
            var r = Run("200 THK RC WALL TYP.", Typ());
            Assert.True(r.Changed);
            Assert.Equal("200 THK RC WALL TYPICAL", r.NewText);
            Assert.Equal(1, r.MatchCount);

            Assert.Equal("TYPICAL handrail", Run("Typ. handrail", Typ()).NewText);
        }

        [Fact]
        public void Match_case_skips_other_case()
        {
            var r = Run("Typ. handrail", Typ() with { MatchCase = true });
            Assert.False(r.Matched);
            Assert.False(r.Changed);
            Assert.Empty(r.EditPasses);
        }

        [Fact]
        public void Plain_find_treats_regex_characters_literally()
        {
            Assert.False(Run("TYPX", Typ()).Matched);
            Assert.Equal("$1 cost", Run("a cost", new ReplaceOptions { Find = "a", Replacement = "$1", WholeWord = true }).NewText);
        }

        [Fact]
        public void Whole_word_ignores_matches_inside_words()
        {
            var o = new ReplaceOptions { Find = "WALL", Replacement = "WL", WholeWord = true };
            Assert.Equal("WL / DRYWALL / WL.", Run("WALL / DRYWALL / WALL.", o).NewText);
        }

        [Fact]
        public void Regex_with_groups()
        {
            var o = new ReplaceOptions { Find = @"@ (\d+)", Replacement = "AT $1 CRS", UseRegex = true };
            Assert.Equal("BRACKET AT 900 CRS TYP.", Run("BRACKET @ 900 TYP.", o).NewText);
        }

        [Fact]
        public void Invalid_regex_is_reported()
        {
            var o = new ReplaceOptions { Find = "(", UseRegex = true };
            Assert.False(ReplaceEngine.TryBuildRegex(o, out _, out string error));
            Assert.StartsWith("Invalid regular expression", error);
            Assert.False(ReplaceEngine.TryBuildRegex(new ReplaceOptions(), out _, out error));
            Assert.Equal("Enter the text to find.", error);
        }

        [Fact]
        public void Trim_collapses_spaces_per_line_and_keeps_line_breaks()
        {
            Assert.Equal("COPING TYPICAL", Run("COPING  TYP. ", Typ(trim: true)).NewText);
            Assert.Equal("ROOF INSULATION,\rTYPICAL ALL ROOFS", Run("  ROOF INSULATION,  \r TYP.  ALL ROOFS", Typ(trim: true)).NewText);
            Assert.Equal("A\r\nTYPICAL", Run("A \r\n TYP.", Typ(trim: true)).NewText);
        }

        [Fact]
        public void Without_trim_spaces_are_kept()
        {
            Assert.Equal("COPING  TYPICAL ", Run("COPING  TYP. ", Typ()).NewText);
        }

        [Fact]
        public void Prefix_and_suffix_only_on_matching_notes_and_suffix_before_trailing_break()
        {
            var o = Typ() with { Prefix = "* ", Suffix = " (R1)" };
            Assert.Equal("* WALL TYPICAL (R1)", Run("WALL TYP.", o).NewText);
            Assert.Equal("* WALL TYPICAL (R1)\r", Run("WALL TYP.\r", o).NewText);
            var none = Run("FFL +3.150", o);
            Assert.False(none.Matched);
            Assert.Equal("FFL +3.150", none.NewText);
        }

        [Fact]
        public void Replacing_with_the_same_text_is_not_a_change()
        {
            var r = Run("WALL TYP.", new ReplaceOptions { Find = "typ.", Replacement = "TYP." });
            Assert.True(r.Matched);
            Assert.False(r.Changed);
            Assert.Empty(r.EditPasses);
        }

        [Fact]
        public void Empty_replacement_deletes_and_trim_cleans_up()
        {
            var r = Run("WALL TYP. ALL", new ReplaceOptions { Find = "TYP.", Replacement = "", TrimSpaces = true });
            Assert.Equal("WALL ALL", r.NewText);
        }

        [Fact]
        public void Segments_mark_only_the_changed_parts()
        {
            var r = Run("200 TYP. WALL", Typ());
            Assert.Equal(new[] { "200 ", "TYP.", " WALL" }, r.OldSegments.Select(s => s.Text));
            Assert.Equal(SegmentKind.Removed, r.OldSegments[1].Kind);
            Assert.Equal(new[] { "200 ", "TYPICAL", " WALL" }, r.NewSegments.Select(s => s.Text));
            Assert.Equal(SegmentKind.Inserted, r.NewSegments[1].Kind);
        }

        [Fact]
        public void Several_matches_give_one_edit_each()
        {
            var r = Run("TYP. A, TYP. B", Typ());
            Assert.Equal("TYPICAL A, TYPICAL B", r.NewText);
            Assert.Equal(2, r.EditPasses[0].Count);
        }

        [Fact]
        public void Zero_length_regex_match_inserts()
        {
            var r = Run("WALL", new ReplaceOptions { Find = "^", Replacement = "NEW ", UseRegex = true });
            Assert.Equal("NEW WALL", r.NewText);
        }

        [Fact]
        public void Slow_pattern_times_out()
        {
            var o = new ReplaceOptions { Find = "(a+)+$", UseRegex = true };
            Assert.True(ReplaceEngine.TryBuildRegex(o, out var rx, out _));
            Assert.Throws<RegexMatchTimeoutException>(() => ReplaceEngine.Run(new string('a', 40) + "!", o, rx));
        }
    }
}
