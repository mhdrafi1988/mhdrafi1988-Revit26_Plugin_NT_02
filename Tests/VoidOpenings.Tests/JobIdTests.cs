using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;
using Xunit;

namespace VoidOpenings.Tests
{
    public class JobIdTests
    {
        [Fact]
        public void Job_id_is_formatted_as_void_year_six_digits()
        {
            Assert.Equal("VOID-2026-000001", JobIdGenerator.Format(2026, 1));
        }

        [Fact]
        public void Next_continues_after_the_highest_existing_number_of_that_year()
        {
            var existing = new[] { "VOID-2026-000002", "VOID-2026-000007", "VOID-2025-000900", "not a job id", null };

            Assert.Equal("VOID-2026-000008", JobIdGenerator.Next(existing, 2026));
        }

        [Fact]
        public void Next_starts_at_one_when_the_year_has_no_ids()
        {
            Assert.Equal("VOID-2027-000001", JobIdGenerator.Next(new[] { "VOID-2026-000007" }, 2027));
        }

        [Fact]
        public void Next_never_falls_below_the_stored_counter()
        {
            Assert.Equal("VOID-2026-000021", JobIdGenerator.Next(new[] { "VOID-2026-000003" }, 2026, lastIssued: 20));
        }

        [Theory]
        [InlineData("VOID-2026-000015", true, 2026, 15)]
        [InlineData("void-2026-000015", true, 2026, 15)]
        [InlineData("VOID-2026-15", false, 0, 0)]
        [InlineData("JOB-2026-000015", false, 0, 0)]
        [InlineData("", false, 0, 0)]
        public void TryParse_accepts_only_well_formed_ids(string text, bool ok, int year, int number)
        {
            Assert.Equal(ok, JobIdGenerator.TryParse(text, out int y, out int n));
            Assert.Equal(year, y);
            Assert.Equal(number, n);
        }
    }
}
