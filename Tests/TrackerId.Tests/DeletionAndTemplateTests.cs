using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TrackerId.Tests
{
    public class DeletionAndTemplateTests
    {
        [Fact]
        public void Deletion_log_round_trips_and_appends()
        {
            string path = Path.Combine(Path.GetTempPath(), "trk_" + Guid.NewGuid().ToString("N"), "m_DeletedTrackerIds.csv");
            var at = new DateTime(2026, 10, 20, 9, 30, 0);
            DeletionLog.Append(path, new[] { new DeletedTrackerId { TrackerId = "A-1", ElementId = 5, DeletedAt = at, User = "Rafi, M" } });
            DeletionLog.Append(path, new[] { new DeletedTrackerId { TrackerId = "A-2", ElementId = 6, DeletedAt = at, User = "x" } });

            var read = DeletionLog.Read(path);
            Assert.Equal(new[] { "A-1", "A-2" }, read.Select(r => r.TrackerId));
            Assert.Equal("Rafi, M", read[0].User);
            Assert.Equal(at, read[0].DeletedAt);
            Assert.Equal(5, read[0].ElementId);
        }

        [Fact]
        public void An_undone_delete_drops_out_of_the_still_missing_list()
        {
            var log = new[]
            {
                new DeletedTrackerId { TrackerId = "A-1", DeletedAt = new DateTime(2026, 1, 1) },
                new DeletedTrackerId { TrackerId = "A-2", DeletedAt = new DateTime(2026, 1, 2) },
                new DeletedTrackerId { TrackerId = "A-2", DeletedAt = new DateTime(2026, 1, 3) }
            };
            var missing = DeletionLog.StillMissing(log, new[] { "a-1" });
            Assert.Single(missing);
            Assert.Equal(new DateTime(2026, 1, 3), missing[0].DeletedAt);
        }

        [Fact]
        public void Health_check_lists_deleted_ids()
        {
            var door = TestData.Door(1, "KEEP-1", "u1");
            var deleted = new List<DeletedTrackerId> { new() { TrackerId = "GONE-1", ElementId = 9, DeletedAt = DateTime.Now, User = "u" } };
            var issues = HealthChecker.Check(new[] { door }, TestData.Holders(new[] { door }), TestData.Format(), TestData.Grids(), null, deleted);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.Deleted && i.TrackerId == "GONE-1");
        }

        [Fact]
        public void Simple_template_builds_level_type_number()
        {
            var template = SegmentCatalog.Templates.Single(t => t.Name.StartsWith("Simple"));
            var format = TestData.Format();
            format.Segments = SegmentCatalog.FromTemplate(template);
            var row = TrackerIdPlanner.Plan(new[] { TestData.Door(1) }, format, TestData.Grids(), new List<TrackerIdHolder>()).Single();
            Assert.Equal("L01-D09-0001", row.ResultId);
            Assert.Equal(SegmentCatalog.DefaultSegments().Count, format.Segments.Count);
        }

        [Fact]
        public void Standard_template_is_the_default_format()
        {
            var segs = SegmentCatalog.FromTemplate(SegmentCatalog.Templates[0]);
            Assert.Equal(SegmentCatalog.DefaultSegments().Select(s => (s.Kind, s.Enabled)), segs.Select(s => (s.Kind, s.Enabled)));
        }
    }
}
