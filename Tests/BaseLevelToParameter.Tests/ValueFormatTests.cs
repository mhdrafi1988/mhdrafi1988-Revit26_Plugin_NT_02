using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;
using Xunit;

namespace BaseLevelToParameter.Tests
{
    public class ValueFormatTests
    {
        [Theory]
        [InlineData(ValueFormat.LevelParenOffset, 450, "Level 02 (+450)")]
        [InlineData(ValueFormat.LevelParenOffset, 0, "Level 02 (0)")]
        [InlineData(ValueFormat.LevelOnly, 450, "Level 02")]
        [InlineData(ValueFormat.LevelSpaceOffset, -150, "Level 02 -150")]
        [InlineData(ValueFormat.LevelParenOffsetMm, 450, "Level 02 (+450 mm)")]
        [InlineData(ValueFormat.LevelSlashOffset, 450, "Level 02 / +450")]
        [InlineData(ValueFormat.LevelBracketOffset, 0, "Level 02 [0]")]
        [InlineData(ValueFormat.LevelCommaOffset, 450, "Level 02, Offset +450")]
        [InlineData(ValueFormat.LevelParenOffsetNoZero, 0.4, "Level 02")]
        [InlineData(ValueFormat.LevelParenOffsetNoZero, -150, "Level 02 (-150)")]
        public void Format_AllPatterns(ValueFormat format, double mm, string expected) =>
            Assert.Equal(expected, ValueFormatter.Format("Level 02", mm, format));

        [Fact]
        public void DefaultMatchesOneZeroFormat() =>
            Assert.Equal(BaseLevelRules.FormatValue("Level 02", 450), ValueFormatter.Format("Level 02", 450, default));

        [Fact]
        public void Custom_ReplacesTokens() =>
            Assert.Equal("L=Level 02 / O=+450mm", ValueFormatter.Format("Level 02", 450, ValueFormat.Custom, "L={Level} / O={Offset}mm"));

        [Theory]
        [InlineData("{Level} ({Offset})", true)]
        [InlineData("{level}", true)]
        [InlineData("", false)]
        [InlineData("({Offset})", false)]
        [InlineData("{Level} {Height}", false)]
        public void ValidateCustom(string pattern, bool ok) =>
            Assert.Equal(ok, ValueFormatter.ValidateCustom(pattern) == null);

        [Fact]
        public void PatternList_HasAllOffsetPatternsAndCustom()
        {
            Assert.Equal(8, ValueFormatter.OffsetPatterns.Count);
            Assert.Equal(ValueFormat.LevelParenOffset, ValueFormatter.OffsetPatterns[0].Format);
        }

        [Fact]
        public void SameValue_IsUpToDate_EvenWithOverwriteOff()
        {
            var e = new ElementFacts { Id = 1, HasLevel = true, LevelName = "Level 02", OffsetMm = 450, CurrentValue = "Level 02 (+450)" };
            Assert.Equal(RowState.UpToDate, BaseLevelRules.EvaluateRow(e, false, false, false, "Level 02 (+450)").State);
            Assert.Equal(RowState.Warning, BaseLevelRules.EvaluateRow(e, false, false, false, "Level 02").State);
            Assert.Equal(RowState.Ready, BaseLevelRules.EvaluateRow(e, false, true, false, "Level 02").State);
        }

        [Fact]
        public void CustomInvalid_AddsBlockedCheckF()
        {
            var t = new TargetFacts { Name = "X", ProjectMatchCount = 1, IsText = true, IsInstance = true, CategoryCount = 1 };
            var e = new[] { new ElementFacts { Id = 1, HasLevel = true, LevelName = "L1" } };
            var checks = BaseLevelRules.EvaluateChecks(t, e, false, ValueFormat.Custom, "{Offset}");
            Assert.Equal(6, checks.Count);
            Assert.Equal(CheckResult.Blocked, checks[5].Result);
            Assert.Equal(5, BaseLevelRules.EvaluateChecks(t, e, false, ValueFormat.LevelOnly, null).Count);
        }

        [Fact]
        public void Summary_AddsUpToDate() =>
            Assert.Equal("5 ready | 1 warnings | 0 blocked · 2 up to date", BaseLevelRules.Summary(5, 1, 0, 2));
    }
}
