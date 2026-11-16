using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;
using Xunit;

namespace VoidOpenings.Tests
{
    public class ClearanceValidationTests
    {
        private static ClearanceInput Card(params ClearanceRuleInput[] rules)
        {
            var input = new ClearanceInput { DefaultClearanceText = "50", OvershootText = "50", RoundUpToText = "0" };
            input.Rules.AddRange(rules);
            return input;
        }

        private static ClearanceRuleInput Row(MepCategory category, string from, string to, string offset,
            ClearanceMode mode = ClearanceMode.PerSide)
            => new ClearanceRuleInput { Category = category, FromText = from, ToText = to, ClearanceText = offset, Mode = mode };

        [Fact]
        public void A_valid_card_gives_a_setup_with_the_rules_in_order()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Conduit, "0", "25", "25"),
                Row(MepCategory.Conduit, "26", "50", "30"),
                Row(MepCategory.Duct, "0", "500", "75", ClearanceMode.Total)));

            Assert.False(result.HasErrors);
            Assert.Empty(result.Issues);
            Assert.Equal(3, result.Setup.Rules.Count);
            Assert.Equal(30, result.Setup.Rules[1].ClearanceMm);
            Assert.Equal(ClearanceMode.Total, result.Setup.Rules[2].Mode);
            Assert.Equal(50, result.Setup.DefaultClearanceMm);
        }

        [Fact]
        public void The_validated_setup_sizes_the_brief_example_conduit_40_with_30_each_side_as_100()
        {
            ClearanceSetup setup = ClearanceValidator.Validate(Card(Row(MepCategory.Conduit, "26", "50", "30"))).Setup;

            ClearanceRule rule = setup.ToRuleSet().Resolve(MepCategory.Conduit, 40);

            Assert.Equal(100, ClearanceFormula.OpeningFor(40, rule.ClearanceMm, rule.Mode, 0));
        }

        [Fact]
        public void An_empty_card_with_valid_options_is_valid_and_has_no_rules()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card());

            Assert.False(result.HasErrors);
            Assert.Empty(result.Setup.Rules);
        }

        [Fact]
        public void Empty_from_is_zero_and_empty_to_is_no_upper_limit()
        {
            ClearanceRule rule = ClearanceValidator.Validate(Card(Row(MepCategory.Pipe, "", " ", "40"))).Setup.Rules.Single();

            Assert.Equal(0, rule.FromMm);
            Assert.False(rule.HasUpperLimit);
            Assert.True(rule.Covers(5000));
        }

        [Fact]
        public void A_decimal_comma_is_read_like_a_decimal_point()
        {
            ClearanceRule rule = ClearanceValidator.Validate(Card(Row(MepCategory.Pipe, "0", "12,5", "7.5"))).Setup.Rules.Single();

            Assert.Equal(12.5, rule.ToMm);
            Assert.Equal(7.5, rule.ClearanceMm);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-5")]
        [InlineData("1e3")]
        [InlineData("1 000")]
        [InlineData("100001")]
        [InlineData("NaN")]
        public void A_value_that_is_not_a_number_in_range_is_an_error_on_its_row(string text)
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Pipe, "0", "50", "25"),
                Row(MepCategory.Pipe, "51", "100", text)));

            Assert.True(result.HasErrors);
            Assert.Null(result.Setup);
            Assert.Empty(result.IssuesFor(0));
            ClearanceIssue issue = Assert.Single(result.IssuesFor(1));
            Assert.Equal(IssueSeverity.Error, issue.Severity);
            Assert.Contains("Offset", issue.Message);
        }

        [Fact]
        public void A_missing_offset_is_an_error()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(Row(MepCategory.Pipe, "0", "50", "")));

            Assert.Equal("Offset is required.", Assert.Single(result.IssuesFor(0)).Message);
        }

        [Fact]
        public void From_above_to_is_an_error()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(Row(MepCategory.Pipe, "100", "50", "25")));

            Assert.True(result.HasErrors);
            Assert.Equal("Size from is larger than Size to.", Assert.Single(result.IssuesFor(0)).Message);
        }

        [Fact]
        public void Every_wrong_value_of_a_row_is_reported()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(Row(MepCategory.Pipe, "x", "y", "z")));

            Assert.Equal(3, result.IssuesFor(0).Count);
            Assert.Equal(3, result.ErrorCount);
        }

        [Fact]
        public void Overlapping_rules_of_one_category_warn_on_the_lower_row_and_can_still_be_saved()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Pipe, "0", "50", "25"),
                Row(MepCategory.Pipe, "40", "100", "40")));

            Assert.False(result.HasErrors);
            Assert.NotNull(result.Setup);
            Assert.Empty(result.IssuesFor(0));
            ClearanceIssue warning = Assert.Single(result.IssuesFor(1));
            Assert.Equal(IssueSeverity.Warning, warning.Severity);
            Assert.Contains("40 to 50 mm", warning.Message);
            Assert.Contains("Rule 1", warning.Message);
        }

        [Fact]
        public void An_open_ended_overlap_is_described_as_and_above()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Pipe, "100", "", "25"),
                Row(MepCategory.Pipe, "200", "", "40")));

            Assert.Contains("200 mm and above", Assert.Single(result.IssuesFor(1)).Message);
        }

        [Fact]
        public void Ranges_that_only_share_an_end_point_do_not_warn()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Pipe, "0", "25", "25"),
                Row(MepCategory.Pipe, "25", "50", "30")));

            Assert.Empty(result.Issues);
        }

        [Fact]
        public void The_same_range_in_two_categories_does_not_warn()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Pipe, "0", "50", "25"),
                Row(MepCategory.Duct, "0", "50", "25")));

            Assert.Empty(result.Issues);
        }

        [Fact]
        public void Overlap_is_checked_against_the_row_numbers_the_user_sees_when_a_row_between_has_an_error()
        {
            ClearanceValidation result = ClearanceValidator.Validate(Card(
                Row(MepCategory.Pipe, "0", "50", "25"),
                Row(MepCategory.Pipe, "bad", "50", "25"),
                Row(MepCategory.Pipe, "10", "20", "30")));

            Assert.Contains("Rule 1", Assert.Single(result.IssuesFor(2)).Message);
        }

        [Theory]
        [InlineData("", "50", "0", "Default offset is required.")]
        [InlineData("50", "", "0", "Overshoot is required.")]
        [InlineData("50", "50", "ten", "Round up to must be")]
        [InlineData("-1", "50", "0", "Default offset must be")]
        public void Wrong_options_are_general_errors(string defaultOffset, string overshoot, string roundUp, string expected)
        {
            var input = new ClearanceInput { DefaultClearanceText = defaultOffset, OvershootText = overshoot, RoundUpToText = roundUp };

            ClearanceValidation result = ClearanceValidator.Validate(input);

            Assert.True(result.HasErrors);
            Assert.Contains(expected, Assert.Single(result.IssuesFor(ClearanceIssue.General)).Message);
        }

        [Fact]
        public void Empty_round_up_means_no_rounding_and_the_options_are_carried_into_the_setup()
        {
            var input = new ClearanceInput
            {
                DefaultClearanceText = "40", DefaultMode = ClearanceMode.Total, OvershootText = "75", RoundUpToText = "",
                IncludeInsulation = false, AxisMode = OpeningAxisMode.FollowMep, MaxCircularAngleDeg = 20,
            };

            ClearanceSetup setup = ClearanceValidator.Validate(input).Setup;

            Assert.Equal(40, setup.DefaultClearanceMm);
            Assert.Equal(ClearanceMode.Total, setup.DefaultMode);
            Assert.Equal(75, setup.Options.OvershootMm);
            Assert.Equal(0, setup.Options.RoundUpToMm);
            Assert.False(setup.Options.IncludeInsulation);
            Assert.Equal(OpeningAxisMode.FollowMep, setup.Options.AxisMode);
            Assert.Equal(20, setup.Options.MaxCircularAngleDeg);
        }

        [Fact]
        public void A_setup_shown_on_the_card_and_validated_again_is_unchanged()
        {
            var setup = new ClearanceSetup(new[]
            {
                new ClearanceRule(MepCategory.Conduit, 0, 25, 25, ClearanceMode.PerSide, OpeningShapePolicy.Circular),
                new ClearanceRule(MepCategory.Duct, 500.5, ClearanceRule.NoUpperLimit, 75, ClearanceMode.Total, OpeningShapePolicy.Rectangular),
            }, 35, ClearanceMode.Total, new OpeningSettings { OvershootMm = 60, RoundUpToMm = 10, IncludeInsulation = false });

            ClearanceSetup again = ClearanceValidator.Validate(ClearanceInput.From(setup)).Setup;

            Assert.Equal(2, again.Rules.Count);
            Assert.Equal(500.5, again.Rules[1].FromMm);
            Assert.False(again.Rules[1].HasUpperLimit);
            Assert.Equal(OpeningShapePolicy.Rectangular, again.Rules[1].Shape);
            Assert.Equal(35, again.DefaultClearanceMm);
            Assert.Equal(10, again.Options.RoundUpToMm);
            Assert.False(again.Options.IncludeInsulation);
        }

        [Theory]
        [InlineData(50, 30, ClearanceMode.PerSide, 0, "50 + 2 × 30 = 110 mm")]
        [InlineData(500, 75, ClearanceMode.Total, 0, "500 + 75 = 575 mm")]
        [InlineData(500, 75, ClearanceMode.Total, 10, "500 + 75 = 575 → 580 mm")]
        [InlineData(40, 30, ClearanceMode.PerSide, 10, "40 + 2 × 30 = 100 mm")]
        public void The_example_shows_the_sum_and_the_rounded_size(double size, double offset, ClearanceMode mode, double roundUp, string expected)
        {
            Assert.Equal(expected, ClearanceFormula.Example(size, offset, mode, roundUp));
        }

        [Fact]
        public void A_rule_example_uses_its_largest_size_or_its_smallest_when_open_ended()
        {
            Assert.Equal("50 + 2 × 30 = 110 mm",
                ClearanceFormula.Example(new ClearanceRule(MepCategory.Conduit, 26, 50, 30, ClearanceMode.PerSide), 0));
            Assert.Equal("200 + 40 = 240 mm",
                ClearanceFormula.Example(new ClearanceRule(MepCategory.Pipe, 200, ClearanceRule.NoUpperLimit, 40, ClearanceMode.Total), 0));
        }
    }
}
