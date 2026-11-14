using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;
using Xunit;

namespace BaseLevelToParameter.Tests
{
    /// <summary>1.2.0: Skip or Rewrite an existing value, per tool default and per element.</summary>
    public class ExistingChoiceTests
    {
        private static TargetFacts Target() => new()
        {
            Name = "Base Level Text",
            ProjectMatchCount = 1,
            IsText = true,
            IsInstance = true,
            IsShared = true,
            ProjectGuid = Guid.Parse("3f2a0000-0000-0000-0000-00000000c9e1"),
            CategoryCount = 1
        };

        private static ElementFacts Valued(long id, string value) => new()
        {
            Id = id, Category = "Walls", HasLevel = true, LevelName = "Level 02", OffsetMm = 450, CurrentValue = value
        };

        [Theory]
        [InlineData(false, ExistingChoice.Default, false)]
        [InlineData(true, ExistingChoice.Default, true)]
        [InlineData(true, ExistingChoice.Skip, false)]
        [InlineData(false, ExistingChoice.Rewrite, true)]
        public void Rewrites_RowChoiceBeatsDefault(bool overwriteDefault, ExistingChoice choice, bool expected) =>
            Assert.Equal(expected, BaseLevelRules.Rewrites(overwriteDefault, choice));

        [Fact]
        public void RowChoice_Rewrite_IsReadyEvenWhenDefaultIsSkip()
        {
            var v = BaseLevelRules.EvaluateRow(Valued(1, "L2"), false, false, ExistingChoice.Rewrite, false, "Level 02 (+450)");
            Assert.Equal(RowState.Ready, v.State);
            Assert.Equal("Ready · rewrites value", v.Text);
        }

        [Fact]
        public void RowChoice_Skip_KeepsValueEvenWhenDefaultIsRewrite()
        {
            var v = BaseLevelRules.EvaluateRow(Valued(1, "L2"), false, true, ExistingChoice.Skip, false, "Level 02 (+450)");
            Assert.Equal(RowState.Warning, v.State);
            Assert.Equal("Skipped · keeps value", v.Text);
        }

        [Fact]
        public void DefaultSkip_KeepsOldStatusText() =>
            Assert.Equal("Skipped · has value",
                BaseLevelRules.EvaluateRow(Valued(1, "L2"), false, false, ExistingChoice.Default, false, "Level 02 (+450)").Text);

        [Fact]
        public void SameValue_StaysUpToDate_WhateverTheChoice() =>
            Assert.Equal(RowState.UpToDate,
                BaseLevelRules.EvaluateRow(Valued(1, "Level 02 (+450)"), false, true, ExistingChoice.Rewrite, false, "Level 02 (+450)").State);

        [Fact]
        public void EmptyValue_IsPlainReady() =>
            Assert.Equal("Ready", BaseLevelRules.EvaluateRow(Valued(1, ""), false, false, ExistingChoice.Default, false, "Level 02 (+450)").Text);

        [Fact]
        public void HasDifferentValue_IgnoresEmptyAndSame()
        {
            Assert.False(BaseLevelRules.HasDifferentValue(Valued(1, ""), "Level 02 (+450)"));
            Assert.False(BaseLevelRules.HasDifferentValue(Valued(1, "Level 02 (+450)"), "Level 02 (+450)"));
            Assert.True(BaseLevelRules.HasDifferentValue(Valued(1, "L2"), "Level 02 (+450)"));
        }

        [Fact]
        public void CheckE_CountsSkippedAndRewritten()
        {
            var elements = new[] { Valued(1, "L2"), Valued(2, "L2"), Valued(3, "Level 02 (+450)") };
            var choices = new Dictionary<long, ExistingChoice> { [2] = ExistingChoice.Rewrite };
            var e = BaseLevelRules.EvaluateChecks(Target(), elements, false, choices, ValueFormat.LevelParenOffset, null)[4];
            Assert.Equal(CheckResult.Warning, e.Result);
            Assert.Contains("2 element(s) already have a different value → 1 skipped, 1 rewritten", e.Detail);
        }

        [Fact]
        public void CheckE_PassesWhenEveryExistingValueIsRewritten()
        {
            var elements = new[] { Valued(1, "L2") };
            var choices = new Dictionary<long, ExistingChoice> { [1] = ExistingChoice.Rewrite };
            var e = BaseLevelRules.EvaluateChecks(Target(), elements, false, choices, ValueFormat.LevelParenOffset, null)[4];
            Assert.Equal(CheckResult.Pass, e.Result);
        }
            // ── 1.3.0: Clear old values, write only new values ──────────────────

        private static ElementFacts NoLevel(long id, string value) => new()
        {
            Id = id, Category = "Generic Models", HasLevel = false, CurrentValue = value
        };

        [Fact]
        public void ClearOld_RewritesEvenWhenRowSaysSkip()
        {
            var v = BaseLevelRules.EvaluateRow(Valued(1, "L2"), false, false, ExistingChoice.Skip, true, false, "Level 02 (+450)");
            Assert.Equal("Ready · rewrites value", v.Text);
        }

        [Fact]
        public void ClearOld_ClearsValueWithoutBaseLevel()
        {
            var v = BaseLevelRules.EvaluateRow(NoLevel(1, "L2"), false, true, ExistingChoice.Default, true, false, null);
            Assert.Equal(RowState.Ready, v.State);
            Assert.Equal("Ready · clears value", v.Text);
        }

        [Fact]
        public void ClearOld_NoLevelAndNoValue_StillSkipped() =>
            Assert.Equal("Skipped · no base level",
                BaseLevelRules.EvaluateRow(NoLevel(1, ""), false, true, ExistingChoice.Default, true, false, null).Text);

        [Fact]
        public void ClearOld_OwnedElement_IsNotCleared() =>
            Assert.Contains("owned by",
                BaseLevelRules.EvaluateRow(new ElementFacts { Id = 1, HasLevel = false, CurrentValue = "L2", OwnedBy = "User B" },
                    false, true, ExistingChoice.Default, true, false, null).Text);

        [Fact]
        public void WithoutClearOld_NoLevelValue_IsSkippedAsBefore() =>
            Assert.Equal("Skipped · no base level",
                BaseLevelRules.EvaluateRow(NoLevel(1, "L2"), false, true, ExistingChoice.Rewrite, false, false, null).Text);

        [Fact]
        public void CheckE_ClearOld_ReportsClearedAndPasses()
        {
            var elements = new[] { Valued(1, "L2"), NoLevel(2, "old") };
            var choices = new Dictionary<long, ExistingChoice> { [1] = ExistingChoice.Skip };
            var e = BaseLevelRules.EvaluateChecks(Target(), elements, true, true, choices, ValueFormat.LevelParenOffset, null)[4];
            Assert.Equal(CheckResult.Pass, e.Result);
            Assert.Contains("1 element(s) already have a different value → rewritten", e.Detail);
            Assert.Contains("1 element(s) with no base level have an old value → cleared", e.Detail);
        }
    }
}
