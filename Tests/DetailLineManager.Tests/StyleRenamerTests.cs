using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DetailLineManager.Tests
{
    public class StyleRenamerTests
    {
        private static List<RenamePlanItem> Items(params string[] names)
            => names.Select((n, i) => new RenamePlanItem { Key = i + 1, CurrentName = n }).ToList();

        [Fact]
        public void Apply_RunsRulesInOrder()
        {
            var rules = new RenameRules { Find = "ls_", Replace = "a-", Prefix = " ", Suffix = "  x", Case = NameCase.Upper, Trim = true };
            Assert.Equal("A-WALL X", StyleRenamer.Apply("LS_Wall", rules));
        }

        [Fact]
        public void Apply_RegexWithGroups()
        {
            var rules = new RenameRules { Find = @"^(\w+)-(\w+)$", Replace = "$2-$1", UseRegex = true };
            Assert.Equal("Wall-A", StyleRenamer.Apply("A-Wall", rules));
        }

        [Fact]
        public void Validate_ReportsBadRegex()
            => Assert.NotNull(StyleRenamer.Validate(new RenameRules { Find = "(", UseRegex = true }));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("A:B")]
        [InlineData("A<B>")]
        [InlineData("A{1}")]
        public void NameProblem_RefusesBadNames(string name) => Assert.NotNull(StyleRenamer.NameProblem(name));

        [Fact]
        public void Plan_FlagsNameOfExistingStyle()
        {
            var items = Items("LS_Grid");
            StyleRenamer.Plan(items, new[] { "LS_Grid", "A-Grid" }, new RenameRules { Find = "LS_", Replace = "A-" }, DuplicateNameRule.Skip);
            Assert.Equal(RenameStatus.Duplicate, items[0].Status);
        }

        [Fact]
        public void Plan_ExistingNameIgnoresCase()
        {
            var items = Items("LS_Grid");
            StyleRenamer.Plan(items, new[] { "LS_Grid", "a-grid" }, new RenameRules { Find = "LS_", Replace = "A-" }, DuplicateNameRule.Skip);
            Assert.Equal(RenameStatus.Duplicate, items[0].Status);
        }

        [Fact]
        public void Plan_FlagsTwoStylesGettingTheSameName()
        {
            var items = Items("LS_Hatch", "ls_hatch");
            StyleRenamer.Plan(items, items.Select(i => i.CurrentName), new RenameRules { Find = "ls_", Replace = "A-" }, DuplicateNameRule.Skip);
            Assert.Equal(1, items.Count(i => i.Status == RenameStatus.WillRename));
            Assert.Equal(1, items.Count(i => i.Status == RenameStatus.Duplicate));
        }

        [Fact]
        public void Plan_AddNumberMakesNamesUnique()
        {
            var items = Items("LS_Hatch", "ls_hatch");
            StyleRenamer.Plan(items, new[] { "LS_Hatch", "ls_hatch", "A-Hatch" }, new RenameRules { Find = "ls_", Replace = "A-" }, DuplicateNameRule.AddNumber);
            Assert.All(items, i => Assert.Equal(RenameStatus.WillRename, i.Status));
            Assert.Equal(new[] { "A-Hatch (2)", "A-hatch (3)" }, items.Select(i => i.NewName).ToArray());
        }

        [Fact]
        public void Plan_AllowsSwapAndCaseOnlyChange()
        {
            var swap = Items("A", "B");
            StyleRenamer.Plan(swap, new[] { "A", "B" }, new RenameRules { Find = "^(A|B)$", Replace = "X$1", UseRegex = true }, DuplicateNameRule.Skip);
            Assert.All(swap, i => Assert.Equal(RenameStatus.WillRename, i.Status));

            var caseOnly = Items("wall");
            StyleRenamer.Plan(caseOnly, new[] { "wall" }, new RenameRules { Case = NameCase.Upper }, DuplicateNameRule.Skip);
            Assert.Equal(RenameStatus.WillRename, caseOnly[0].Status);
        }

        [Fact]
        public void Plan_SecondClaimOfANewNameIsDuplicate()
        {
            var items = Items("A", "X");
            var rules = new RenameRules { Find = "^A$|^X$", Replace = "Y", UseRegex = true };
            StyleRenamer.Plan(items, new[] { "A", "X", "Z" }, rules, DuplicateNameRule.Skip);
            Assert.Equal(RenameStatus.WillRename, items[0].Status);
            Assert.Equal(RenameStatus.Duplicate, items[1].Status);
        }

        [Fact]
        public void Plan_BuiltInAndUnchanged()
        {
            var items = new List<RenamePlanItem>
            {
                new() { Key = 1, CurrentName = "Thin Lines", IsBuiltIn = true },
                new() { Key = 2, CurrentName = "A-Roof" }
            };
            StyleRenamer.Plan(items, new[] { "Thin Lines", "A-Roof" }, new RenameRules { Find = "LS_", Replace = "A-" }, DuplicateNameRule.Skip);
            Assert.Equal(RenameStatus.BuiltIn, items[0].Status);
            Assert.Equal(RenameStatus.Unchanged, items[1].Status);
        }

        [Fact]
        public void Plan_NameOfAStyleThatStaysIsDuplicate()
        {
            // "B" keeps its name (the rules leave it), so "A" → "B" clashes with it.
            var items = Items("A", "B");
            var rules = new RenameRules { Find = "^A$", Replace = "B", UseRegex = true };
            StyleRenamer.Plan(items, new[] { "A", "B" }, rules, DuplicateNameRule.Skip);
            Assert.Equal(RenameStatus.Duplicate, items[0].Status);
            Assert.Equal(RenameStatus.Unchanged, items[1].Status);
        }
    }
}
