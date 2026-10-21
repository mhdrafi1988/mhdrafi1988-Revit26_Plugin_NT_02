using System.IO;
using System.Linq;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;
using Xunit;

namespace SharedParamStudio.Tests
{
    public class SpTests
    {
        private const string G1 = "11111111-1111-1111-1111-111111111111";
        private const string G2 = "22222222-2222-2222-2222-222222222222";
        private const string G3 = "33333333-3333-3333-3333-333333333333";

        private static string Text(params string[] paramLines) =>
            "# comment\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\nGROUP\t1\tAlpha\r\nGROUP\t2\tBeta\r\n" +
            "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOKEY\r\n" +
            string.Join("\r\n", paramLines) + "\r\n";

        private static string P(string guid, string name, string type = "TEXT", int group = 1)
            => $"PARAM\t{guid}\t{name}\t{type}\t\t{group}\t1\tdesc\t1\t0";

        [Fact]
        public void Parse_ReadsAllColumnsAndGroups()
        {
            var f = SpFileReader.Parse(Text(P(G1, "Door_Code", "LENGTH", 2)));
            var p = Assert.Single(f.Params);
            Assert.Equal("Door_Code", p.Name);
            Assert.Equal("LENGTH", p.DataType);
            Assert.Equal("Beta", p.GroupName);
            Assert.Equal("desc", p.Description);
            Assert.Equal(new[] { "Alpha", "Beta" }, f.Groups);
        }

        [Fact]
        public void RoundTrip_KeepsGuidsNamesAndGroups()
        {
            var f = SpFileReader.Parse(Text(P(G1, "A"), P(G2, "B", "YESNO", 2)));
            var again = SpFileReader.Parse(SpFileWriter.ToText(f));
            Assert.Equal(f.Params.Select(p => (p.Guid, p.Name, p.DataType, p.GroupName)),
                         again.Params.Select(p => (p.Guid, p.Name, p.DataType, p.GroupName)));
        }

        [Fact]
        public void Write_KeepsBackupAndEncoding()
        {
            string path = Path.Combine(Path.GetTempPath(), "sps_" + System.Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                var f = SpFileReader.Parse(Text(P(G1, "A")));
                SpFileWriter.Write(f, path);
                SpFileWriter.Write(f, path);
                Assert.True(File.Exists(path + ".bak"));
                Assert.Equal("A", SpFileReader.Read(path).Params.Single().Name);
            }
            finally
            {
                foreach (var x in new[] { path, path + ".bak", path + ".tmp" }) if (File.Exists(x)) File.Delete(x);
            }
        }

        [Fact]
        public void Duplicates_FindsGuidAndNameSets_AndRemovesLaterOnes()
        {
            var f = SpFileReader.Parse(Text(P(G1, "A"), P(G1, "A2"), P(G2, "a", "LENGTH"), P(G3, "Other")));
            var r = DuplicateAnalyzer.Analyze(f);
            Assert.Equal(2, r.SetCount);
            Assert.Contains(r.Items, i => i.Kind == DuplicateKind.SameGuid && !i.IsFirst && i.Param.Name == "A2");
            Assert.Contains(r.Items, i => i.Kind == DuplicateKind.SameName && i.DataTypesDiffer);
            var clean = DuplicateAnalyzer.RemoveDuplicates(f, r);
            Assert.Equal(new[] { "A", "Other" }, clean.Params.Select(p => p.Name));
            Assert.Equal(4, f.Params.Count);
        }

        [Fact]
        public void Duplicates_CleanFile_ReportsNothing()
        {
            var r = DuplicateAnalyzer.Analyze(SpFileReader.Parse(Text(P(G1, "A"), P(G2, "B"))));
            Assert.Empty(r.Items);
        }

        [Fact]
        public void Merge_AddsNewAndCountsIdentical()
        {
            var a = SpFileReader.Parse(Text(P(G1, "A")));
            var b = SpFileReader.Parse(Text(P(G1, "A"), P(G2, "B", "TEXT", 2)));
            var r = SpMerger.Merge(a, b, ConflictPolicy.KeepFirst);
            Assert.Equal(1, r.Identical);
            Assert.Equal(1, r.AddedFromSecond);
            Assert.Equal(0, r.Conflicts);
            Assert.Equal(new[] { "A", "B" }, r.Merged.Params.Select(p => p.Name));
            Assert.Contains("Beta", r.Merged.Groups);
        }

        [Theory]
        [InlineData(ConflictPolicy.KeepFirst, 1)]
        [InlineData(ConflictPolicy.KeepSecond, 1)]
        [InlineData(ConflictPolicy.KeepBoth, 2)]
        public void Merge_NameConflict_FollowsPolicy(ConflictPolicy policy, int expectedCount)
        {
            var a = SpFileReader.Parse(Text(P(G1, "Width", "LENGTH")));
            var b = SpFileReader.Parse(Text(P(G2, "width", "TEXT")));
            var r = SpMerger.Merge(a, b, policy);
            Assert.Equal(1, r.Conflicts);
            Assert.Equal(expectedCount, r.Merged.Params.Count);
            if (policy == ConflictPolicy.KeepSecond) Assert.Equal(G2, r.Merged.Params.Single().Guid);
            if (policy == ConflictPolicy.KeepFirst) Assert.Equal(G1, r.Merged.Params.Single().Guid);
            if (policy == ConflictPolicy.KeepBoth) Assert.Contains(r.Merged.Params, p => p.Name == "width_2");
        }

        [Fact]
        public void Merge_GuidConflict_KeepBothFallsBackToFirst()
        {
            var a = SpFileReader.Parse(Text(P(G1, "Width")));
            var b = SpFileReader.Parse(Text(P(G1, "Depth")));
            var r = SpMerger.Merge(a, b, ConflictPolicy.KeepBoth);
            Assert.Equal("Width", r.Merged.Params.Single().Name);
            Assert.Equal(1, r.Conflicts);
        }

        [Fact]
        public void Merge_NewParameters_GetGuidAndSkipClashes()
        {
            var a = SpFileReader.Parse(Text(P(G1, "A")));
            var b = SpFileReader.Parse(Text());
            var add = new[]
            {
                new SpParam { Name = "Fresh", DataType = "YESNO", GroupName = "New" },
                new SpParam { Name = "a" },
                new SpParam { Name = "Bad:Name" }
            };
            var r = SpMerger.Merge(a, b, ConflictPolicy.KeepFirst, add);
            Assert.Equal(1, r.NewParameters);
            var fresh = r.Merged.Params.Single(p => p.Name == "Fresh");
            Assert.True(System.Guid.TryParse(fresh.Guid, out _));
            Assert.Contains("New", r.Merged.Groups);
            Assert.Equal(2, r.Notes.Count(n => n.IsWarning));
        }

        [Theory]
        [InlineData("Good_Name", true)]
        [InlineData("", false)]
        [InlineData(" lead", false)]
        [InlineData("a:b", false)]
        public void ValidateName(string name, bool ok)
            => Assert.Equal(ok, SpKeys.ValidateName(name) == null);
    }
}
