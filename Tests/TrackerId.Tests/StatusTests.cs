using ClosedXML.Excel;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TrackerId.Tests
{
    public class StatusTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "trackerid-tests-" + Guid.NewGuid().ToString("N"));

        public StatusTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* temp folder */ }
        }

        [Fact]
        public void Csv_with_aliases_and_semicolons_keeps_the_latest_row_per_id()
        {
            string path = Path.Combine(_dir, "status.csv");
            File.WriteAllText(path,
                "Tracker ID;Material Status;Progress %;Status Date\n" +
                "ID-1;Ordered;10%;2026-10-01\n" +
                "id-1;Installed;100;2026-10-15\n" +
                "ID-1;Delivered;50;2026-10-05\n" +
                "ID-2;Issue;abc;not a date\n");

            var result = StatusFileReader.Read(path);

            Assert.Equal(4, result.RowCount);
            Assert.Equal(2, result.Records.Count);
            var one = result.Records["ID-1"];
            Assert.Equal("Installed", one.Status);
            Assert.Equal(100, one.Progress);
            Assert.Equal("2026-10-15", one.DateText);
            Assert.Null(result.Records["ID-2"].Progress);
            Assert.Contains(result.Warnings, w => w.Contains("not a number"));
        }

        [Fact]
        public void Excel_percent_cells_and_dates_are_read()
        {
            string path = Path.Combine(_dir, "status.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Status");
                ws.Cell(1, 1).Value = "Tracker_ID";
                ws.Cell(1, 2).Value = "Status";
                ws.Cell(1, 3).Value = "Progress";
                ws.Cell(1, 4).Value = "Date";
                ws.Cell(2, 1).Value = "ID-1";
                ws.Cell(2, 2).Value = "Delivered";
                ws.Cell(2, 3).Value = 0.75;
                ws.Cell(2, 3).Style.NumberFormat.Format = "0%";
                ws.Cell(2, 4).Value = new DateTime(2026, 10, 20);
                wb.SaveAs(path);
            }

            var record = StatusFileReader.Read(path).Records["ID-1"];
            Assert.Equal("Delivered", record.Status);
            Assert.Equal(75, record.Progress);
            Assert.Equal("2026-10-20", record.DateText);
        }

        [Fact]
        public void A_file_without_status_column_is_rejected()
        {
            string path = Path.Combine(_dir, "bad.csv");
            File.WriteAllText(path, "Tracker_ID,Progress\nID-1,10\n");
            Assert.Throws<InvalidDataException>(() => StatusFileReader.Read(path));
        }

        [Fact]
        public void Merge_sets_changed_skips_unchanged_and_never_clears_missing_rows()
        {
            var file = StatusFileReader.FromRows(Rows(new[] { "Tracker_ID", "Status", "Progress" },
                new[] { "A", "Installed", "100" }, new[] { "B", "Ordered", "" }, new[] { "GONE", "Ordered", "" }));

            var a = TestData.Door(1, "A");
            var b = TestData.Door(2, "B");
            b.StoredStatus = "Ordered";
            var c = TestData.Door(3, "C");
            var locked = TestData.Door(4, "A2");
            locked.LockReason = "Owned by Sam.";
            var none = TestData.Door(5);

            var plan = StatusMerger.Plan(new[] { a, b, c, locked, none }, file);

            Assert.Equal(4, plan.Count);
            Assert.Equal(StatusChange.Set, plan.Single(p => p.Element == a).Change);
            Assert.Equal(StatusChange.Unchanged, plan.Single(p => p.Element == b).Change);
            Assert.Equal(StatusChange.NoRow, plan.Single(p => p.Element == c).Change);
            Assert.Equal(StatusChange.Locked, plan.Single(p => p.Element == locked).Change);
            Assert.Equal(new[] { "GONE" }, StatusMerger.IdsNotInModel(file, TestData.Holders(new[] { a, b, c })));
        }

        [Fact]
        public void Colour_table_adds_new_statuses_and_drops_repeats()
        {
            var table = new List<StatusColour>
            {
                new() { Status = "Installed", Color = "#00ff00" },
                new() { Status = "installed", Color = "#123456" },
                new() { Status = "Ordered", Color = "nonsense" }
            };
            var done = StatusColours.Complete(table, new[] { "Installed", "Snagging", "" });

            Assert.Equal(new[] { "Installed", "Ordered", "Snagging" }, done.Select(d => d.Status));
            Assert.Equal("#00FF00", done[0].Color);
            Assert.True(StatusColours.TryParse(done[1].Color, out _, out _, out _));
            Assert.NotEqual(done[1].Color, done[2].Color);
        }

        private static List<StatusFileReader.StatusCells> Rows(params string[][] rows) =>
            rows.Select(r =>
            {
                var cells = new StatusFileReader.StatusCells();
                cells.Cells.AddRange(r);
                return cells;
            }).ToList();
    }
}
