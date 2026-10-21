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
    public class HealthAndExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "trackerid-tests-" + Guid.NewGuid().ToString("N"));

        public HealthAndExportTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* temp folder */ }
        }

        [Fact]
        public void Health_check_reports_every_kind_of_problem()
        {
            var missing = TestData.Door(1);
            var original = TestData.Door(2, "DUP", "u2");
            var copy = TestData.Door(3, "DUP", "u2");
            var moved = TestData.Door(4, "MOVED", "u4");
            moved.StoredLevel = "L05";
            var far = TestData.Door(5, "FAR", "u5", x: 4, y: 4);
            var elements = new[] { missing, original, copy, moved, far };

            var status = StatusFileReader.FromRows(new List<StatusFileReader.StatusCells>
            {
                Row("Tracker_ID", "Status"), Row("DUP", "Installed"), Row("MOVED", "Ordered"), Row("ELSEWHERE", "Ordered")
            });

            var format = TestData.Format();
            format.GridToleranceM = 3;
            var issues = HealthChecker.Check(elements, TestData.Holders(elements), format, TestData.Grids(), status);

            Assert.Contains(issues, i => i.Kind == HealthIssueKind.MissingId && i.ElementId == 1);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.Copy && i.ElementId == 3);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.DuplicateId && i.ElementId == 2);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.Moved && i.ElementId == 4);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.FarFromGrid && i.ElementId == 5);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.NoStatus && i.ElementId == 5);
            Assert.Contains(issues, i => i.Kind == HealthIssueKind.IdNotInModel && i.TrackerId == "ELSEWHERE");
            Assert.DoesNotContain(issues, i => i.Kind == HealthIssueKind.NoStatus && i.ElementId == 2);
        }

        [Fact]
        public void Csv_keeps_the_1_0_columns_first_and_appends_status_columns()
        {
            string[] v1 =
            {
                "Tracker_ID", "Category", "Family", "Type", "Type_Mark", "Zone", "Level", "Grid", "Count", "Length_m", "Area_m2",
                "Volume_m3", "Revit_ElementId", "Revit_UniqueId", "Model", "Export_Date", "Schema_Version", "Model_Code"
            };
            Assert.Equal(v1, CsvExportService.Columns.Take(v1.Length));
            Assert.Equal(new[] { "Status", "Progress_Pct", "Status_Date" }, CsvExportService.Columns.Skip(v1.Length));

            string csv = CsvExportService.Build(new[] { new ExportRow { TrackerId = "A", Status = "Installed", ProgressPct = 100, StatusDate = "2026-10-20" } },
                "NT_02", "ARC", new DateTime(2026, 10, 20, 9, 0, 0));
            Assert.EndsWith(",2,ARC,Installed,100,2026-10-20", csv.Split('\n')[1].TrimEnd('\r'));
        }

        [Fact]
        public void Status_template_is_created_then_only_appended()
        {
            string path = Path.Combine(_dir, "Tracker_Status.xlsx");
            var first = ExcelExportService.WriteStatusTemplate(path, new[] { new TemplateRow { TrackerId = "A", Category = "Doors" } }, new[] { "Ordered", "Installed" });
            Assert.True(first.Created);
            Assert.Equal(1, first.Added);

            // The site team fills in a status, then new elements get IDs.
            using (var wb = new XLWorkbook(path))
            {
                wb.Worksheet("Status").Cell(2, 2).Value = "Installed";
                wb.Save();
            }
            var second = ExcelExportService.WriteStatusTemplate(path, new[] { new TemplateRow { TrackerId = "a" }, new TemplateRow { TrackerId = "B" } }, new[] { "Ordered" });

            Assert.False(second.Created);
            Assert.Equal(1, second.Added);
            Assert.Equal(1, second.AlreadyListed);
            using var check = new XLWorkbook(path);
            var ws = check.Worksheet("Status");
            Assert.Equal("Installed", ws.Cell(2, 2).GetString());
            Assert.Equal("B", ws.Cell(3, 1).GetString());
            Assert.True(ws.DataValidations.Any());
        }

        [Fact]
        public void Excel_export_has_the_same_columns_as_the_csv()
        {
            string path = Path.Combine(_dir, "Tracker_Elements.xlsx");
            ExcelExportService.WriteElements(path, new[] { new ExportRow { TrackerId = "A", LengthM = 1.23456 } }, "NT_02", "ARC", DateTime.Now, false);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Elements");
            Assert.Equal(CsvExportService.Columns, Enumerable.Range(1, CsvExportService.Columns.Count).Select(c => ws.Cell(1, c).GetString()));
            Assert.Equal(1.235, ws.Cell(2, 10).GetDouble(), 3);
        }

        private static StatusFileReader.StatusCells Row(params string[] cells)
        {
            var row = new StatusFileReader.StatusCells();
            row.Cells.AddRange(cells);
            return row;
        }
    }
}
