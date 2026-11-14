using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core;
using Xunit;

namespace BaseLevelToParameter.Tests
{
    public class BaseLevelRulesTests
    {
        [Theory]
        [InlineData(450, "+450")]
        [InlineData(-150, "-150")]
        [InlineData(0, "0")]
        [InlineData(0.4, "0")]
        [InlineData(-0.4, "0")]
        [InlineData(2849.6, "+2850")]
        [InlineData(2.5, "+3")]
        [InlineData(-2.5, "-3")]
        public void FormatOffset_RoundsToWholeMillimetresWithSign(double mm, string expected) =>
            Assert.Equal(expected, BaseLevelRules.FormatOffset(mm));

        [Fact]
        public void FormatValue_UsesLevelAndOffset() =>
            Assert.Equal("Level 02 (+450)", BaseLevelRules.FormatValue("Level 02", 450));

        [Fact]
        public void FormatValue_ZeroOffset() =>
            Assert.Equal("Level 01 (0)", BaseLevelRules.FormatValue("Level 01", 0));

        [Theory]
        [InlineData("Base Level Text", true)]
        [InlineData("", false)]
        [InlineData(" Lead", false)]
        [InlineData("Twenty-six characters long", false)]
        [InlineData("Twenty-five characters ok", true)]
        [InlineData("Bad:Name", false)]
        [InlineData("Bad[Name]", false)]
        [InlineData("Bad~Name", false)]
        public void ValidateNewName(string name, bool ok) =>
            Assert.Equal(ok, BaseLevelRules.ValidateNewName(name) == null);

        private static TargetFacts ExistingTarget() => new()
        {
            Name = "Base Level Text",
            ProjectMatchCount = 1,
            IsText = true,
            IsInstance = true,
            IsShared = true,
            ProjectGuid = Guid.Parse("3f2a0000-0000-0000-0000-00000000c9e1"),
            CategoryCount = 2
        };

        private static ElementFacts Ready(long id) => new() { Id = id, Category = "Walls", HasLevel = true, LevelName = "Level 01", OffsetMm = 0 };

        [Fact]
        public void ExistingTextInstance_AllPass()
        {
            var checks = BaseLevelRules.EvaluateChecks(ExistingTarget(), new[] { Ready(1) }, false);
            Assert.All(checks, c => Assert.Equal(CheckResult.Pass, c.Result));
        }

        [Fact]
        public void ExistingTypeParameter_IsBlocked()
        {
            var t = ExistingTarget();
            t.IsInstance = false;
            var checks = BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false);
            Assert.Equal(CheckResult.Blocked, checks[1].Result);
            Assert.True(BaseLevelRules.AnyBlocked(checks));
        }

        [Fact]
        public void ExistingWrongDataType_IsBlocked()
        {
            var t = ExistingTarget();
            t.IsText = false;
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[1].Result);
        }

        [Fact]
        public void MissingCategories_IsWarning()
        {
            var t = ExistingTarget();
            t.MissingCategories.AddRange(new[] { "Ducts", "Pipes" });
            var b = BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[1];
            Assert.Equal(CheckResult.Warning, b.Result);
            Assert.Contains("Ducts and Pipes", b.Detail);
        }

        [Fact]
        public void UnknownName_NotNew_IsBlocked()
        {
            var t = new TargetFacts { Name = "Nope", CategoryCount = 1 };
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[1].Result);
        }

        [Fact]
        public void NewParameter_NoFile_IsBlocked()
        {
            var t = new TargetFacts { Name = "Base Level Info", IsNew = true, CategoryCount = 1 };
            var c = BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[2];
            Assert.Equal(CheckResult.Blocked, c.Result);
        }

        [Fact]
        public void NewParameter_FileHasSameNameNotText_IsBlocked()
        {
            var t = new TargetFacts
            {
                Name = "Base Level Info", IsNew = true, CategoryCount = 1, FilePath = "x.txt", FileExists = true, FileWritable = true,
                FileEntry = new SharedParamFileEntry { Name = "Base Level Info", DataType = "LENGTH" }
            };
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[2].Result);
        }

        [Fact]
        public void NewParameter_TooLong_IsBlocked()
        {
            var t = new TargetFacts { Name = new string('x', 26), IsNew = true, CategoryCount = 1, FilePath = "x.txt", FileExists = true, FileWritable = true };
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[0].Result);
        }

        private static TargetFacts NewWithFileEntry() => new()
        {
            Name = "Base Level Info", IsNew = true, CategoryCount = 1, FilePath = "x.txt", FileExists = true, FileWritable = true,
            FileEntry = new SharedParamFileEntry { Name = "Base Level Info", DataType = "TEXT", Guid = Guid.NewGuid(), GroupName = "Data" },
            FileMatchCount = 1
        };

        [Fact]
        public void NewParameter_ReusesFileEntry_Passes() =>
            Assert.Equal(CheckResult.Pass, BaseLevelRules.EvaluateChecks(NewWithFileEntry(), new[] { Ready(1) }, false)[2].Result);

        [Fact]
        public void NewParameter_DuplicateNameInFile_IsBlocked()
        {
            var t = NewWithFileEntry();
            t.FileMatchCount = 2;
            var c = BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[2];
            Assert.Equal(CheckResult.Blocked, c.Result);
            Assert.Contains("2 times", c.Detail);
        }

        [Fact]
        public void NewParameter_FileGuidUsedByOtherProjectParameter_IsBlocked()
        {
            var t = NewWithFileEntry();
            t.FileGuidUsedBy = "Old Level Note";
            var c = BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[2];
            Assert.Equal(CheckResult.Blocked, c.Result);
            Assert.Contains("Old Level Note", c.Detail);
        }

        [Fact]
        public void ExistingShared_GuidMismatchInFile_IsBlocked()
        {
            var t = ExistingTarget();
            t.FilePath = "x.txt";
            t.FileExists = true;
            t.FileEntry = new SharedParamFileEntry { Name = t.Name, DataType = "TEXT", Guid = Guid.NewGuid() };
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[2].Result);
        }

        [Fact]
        public void UnsupportedCategory_IsWarning_AllUnsupported_IsBlocked()
        {
            var t = ExistingTarget();
            t.UnsupportedCategories.Add("Lines");
            Assert.Equal(CheckResult.Warning, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[3].Result);
            t.CategoryCount = 1;
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[3].Result);
        }

        [Fact]
        public void NothingTicked_IsBlocked() =>
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(ExistingTarget(), new List<ElementFacts>(), false)[3].Result);

        [Fact]
        public void ExistingValues_WarnWhenOverwriteOff_PassWhenOn()
        {
            var e = Ready(1);
            e.CurrentValue = "Level 01 (+300)";
            Assert.Equal(CheckResult.Warning, BaseLevelRules.EvaluateChecks(ExistingTarget(), new[] { e }, false)[4].Result);
            Assert.Equal(CheckResult.Pass, BaseLevelRules.EvaluateChecks(ExistingTarget(), new[] { e }, true)[4].Result);
        }

        [Fact]
        public void ReadOnlyDocument_IsBlocked()
        {
            var t = ExistingTarget();
            t.IsReadOnly = true;
            Assert.Equal(CheckResult.Blocked, BaseLevelRules.EvaluateChecks(t, new[] { Ready(1) }, false)[4].Result);
        }

        [Fact]
        public void Rows_FollowRules()
        {
            Assert.Equal(RowState.Ready, BaseLevelRules.EvaluateRow(Ready(1), false, false, false).State);
            Assert.Equal(RowState.Blocked, BaseLevelRules.EvaluateRow(Ready(1), true, false, false).State);
            Assert.Equal(RowState.Warning, BaseLevelRules.EvaluateRow(new ElementFacts { HasLevel = false }, false, false, false).State);

            var owned = Ready(2); owned.OwnedBy = "User B";
            Assert.Contains("User B", BaseLevelRules.EvaluateRow(owned, false, false, false).Text);

            var grouped = Ready(3); grouped.InGroup = true;
            Assert.Equal(RowState.Warning, BaseLevelRules.EvaluateRow(grouped, false, false, false).State);
            Assert.Equal(RowState.Ready, BaseLevelRules.EvaluateRow(grouped, false, false, true).State);

            var valued = Ready(4); valued.CurrentValue = "x";
            Assert.Equal(RowState.Warning, BaseLevelRules.EvaluateRow(valued, false, false, false).State);
            Assert.Equal(RowState.Ready, BaseLevelRules.EvaluateRow(valued, false, true, false).State);
        }

        [Fact]
        public void Summaries() 
        {
            Assert.Equal("121 ready | 21 warnings | 0 blocked", BaseLevelRules.Summary(121, 21, 0));
            Assert.Equal("121 assigned | 21 skipped | 0 failed", BaseLevelRules.RunSummary(121, 21, 0));
        }
    }

    public class SharedParamFileReaderTests
    {
        private const string Sample =
            "# This is a Revit shared parameter file.\r\n" +
            "*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n" +
            "*GROUP\tID\tNAME\r\nGROUP\t1\tData\r\nGROUP\t2\tIdentity\r\n" +
            "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n" +
            "PARAM\t3f2a1111-2222-3333-4444-55556666c9e1\tBase Level Text\tTEXT\t\t1\t1\t\t1\t0\r\n" +
            "PARAM\t9b0c1111-2222-3333-4444-555566667777\tSlab Depth\tLENGTH\t\t2\t1\t\t1\t0\r\n";

        [Fact]
        public void Parse_ReadsParamsAndGroupNames()
        {
            var list = SharedParamFileReader.Parse(Sample);
            Assert.Equal(2, list.Count);
            var text = SharedParamFileReader.Find(list, "base level text");
            Assert.NotNull(text);
            Assert.True(text.IsText);
            Assert.Equal("Data", text.GroupName);
            Assert.False(SharedParamFileReader.Find(list, "Slab Depth").IsText);
            Assert.Null(SharedParamFileReader.Find(list, "Missing"));
        }

        [Fact]
        public void FindAll_ReturnsDuplicates()
        {
            var list = SharedParamFileReader.Parse(Sample + "PARAM\t11111111-2222-3333-4444-555566667777\tBase Level Text\tTEXT\t\t2\t1\t\t1\t0\r\n");
            Assert.Equal(2, SharedParamFileReader.FindAll(list, "Base Level Text").Count);
        }

        [Fact]
        public void GroupNames_InFileOrder() =>
            Assert.Equal(new[] { "Data", "Identity" }, SharedParamFileReader.GroupNames(Sample).ToArray());
    }
}
