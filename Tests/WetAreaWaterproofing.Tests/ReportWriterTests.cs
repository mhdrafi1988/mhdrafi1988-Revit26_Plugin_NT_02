using ClosedXML.Excel;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Models;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Report;
using Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Run;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static WetAreaWaterproofing.Tests.LoopRepairTests;

namespace WetAreaWaterproofing.Tests
{
    public class ReportWriterTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "wp-report-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { /* temp folder */ }
        }

        private static RoomCandidate Candidate(string no, List<Seg> loop = null, string owner = null, string matched = "toilet")
            => new RoomCandidate
            {
                Row = new RoomRow
                {
                    RoomKey = "Host:" + no, RoomId = 1000 + int.Parse(no), Number = no, Name = "Room " + no, Source = "Host",
                    Level = "L1", LevelElevationMm = 3000, AreaM2 = 12, Matched = matched, SlabId = 42, SlabTopMm = 3150,
                    Owner = owner, Phase = "New Construction", Workset = "WP"
                },
                Outline = LoopRepair.Evaluate(new[] { loop ?? Rect() }, 12, Defaults, null),
                HostLevelId = 7, HostLevelElevationMm = 3000
            };

        private static RunReport Build()
        {
            var rooms = new[]
            {
                Candidate("101"), Candidate("102", RectWithGap(3)), Candidate("103", owner: "j.smith"),
                Candidate("104"), Candidate("105", matched: null)
            };
            var ticked = new[] { "Host:101", "Host:102", "Host:103" };
            var plan = RunPlanner.Plan(rooms.Where(r => ticked.Contains(r.Row.RoomKey)), Defaults, "20261114-153007");
            var created = new Dictionary<string, CreatedRoof>
            {
                ["Host:101"] = new CreatedRoof(9001, 12.0, false),
                ["Host:102"] = new CreatedRoof(9002, 12.0, true)
            };
            var summary = new ReportSummary
            {
                RunId = plan.RunId, Started = new DateTime(2026, 11, 14, 15, 30, 7), User = "tester", Model = "Sample Model",
                PluginVersion = "7.11.0", ToolVersion = "1.0.0", DurationSeconds = 3.5
            };
            return RunReport.Build(summary, rooms, ticked, plan, created, plan.Issues, Defaults);
        }

        [Fact]
        public void BuildListsTickedAndUntickedWetRoomsOnly()
        {
            var report = Build();
            Assert.Equal(new[] { "101", "102", "103", "104" }, report.Rooms.Select(r => r.Number));
            Assert.Equal(new[] { "Created", "Replaced", "Skipped", "Not ticked" }, report.Rooms.Select(r => r.Status));
            Assert.Contains("Owned by j.smith", report.Rooms[2].Reason);
            Assert.Equal(9001, report.Rooms[0].RoofId);
            Assert.Equal("Clean", report.Rooms[0].BoundaryResult);
            Assert.StartsWith("Repaired", report.Rooms[1].BoundaryResult);
            Assert.Equal(1, report.Summary.RoofsCreated);
            Assert.Equal(1, report.Summary.RoofsReplaced);
            Assert.Equal(1, report.Summary.Skipped);
        }

        [Fact]
        public void BuildListsEveryRepairAttempt()
        {
            var repairs = Build().Repairs.Where(r => r.Number == "102").ToList();
            Assert.Contains(repairs, r => r.Method == 2 && r.Result == "Failed");
            Assert.Contains(repairs, r => r.Method == 4 && r.Result == "Passed" && r.X.HasValue);
        }

        [Fact]
        public void FileNameFollowsThePattern()
            => Assert.Equal("WP_Report_Sample_Model_20261114_1530.xlsx",
                ReportWriter.FileName("Sample Model", new DateTime(2026, 11, 14, 15, 30, 7)));

        [Fact]
        public void FileNameDropsCharactersWindowsRejects()
            => Assert.Equal("WP_Report_A_B_C_20261114_1530.xlsx",
                ReportWriter.FileName("A:B/C", new DateTime(2026, 11, 14, 15, 30, 7)));

        [Fact]
        public void WritesSheetsInOrderWithFixedHeaders()
        {
            string path = ReportWriter.Write(Build(), _folder);
            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            Assert.Equal(new[] { "Summary", "Rooms", "Boundary repair", "Issues" }, wb.Worksheets.Select(w => w.Name));
            var rooms = wb.Worksheet("Rooms");
            var headers = Enumerable.Range(1, ReportWriter.RoomColumns.Count).Select(c => rooms.Cell(1, c).GetString()).ToArray();
            Assert.Equal(new[]
            {
                "Room ElementId", "Source", "Level", "Number", "Name", "Room area m²", "Matched keyword", "Slab ElementId",
                "Slab top mm", "Roof ElementId", "Roof area m²", "Area difference %", "Boundary result", "Status", "Reason",
                "Phase", "Workset", "Group", "Scale", "Run ID"
            }, headers);
            Assert.True(rooms.Cell(1, 1).Style.Font.Bold);
            Assert.True(rooms.AutoFilter.IsEnabled);
            Assert.Equal(1, rooms.SheetView.SplitRow);
        }

        [Fact]
        public void ElementIdsAreNumbersAndStatusIsColoured()
        {
            using var wb = new XLWorkbook(ReportWriter.Write(Build(), _folder));
            var rooms = wb.Worksheet("Rooms");
            Assert.Equal(XLDataType.Number, rooms.Cell(2, 1).DataType);
            Assert.Equal(1101, rooms.Cell(2, 1).GetValue<long>());
            Assert.Equal(9001, rooms.Cell(2, 10).GetValue<long>());
            int status = ReportWriter.RoomColumns.ToList().IndexOf("Status") + 1;
            Assert.Equal(XLColor.FromHtml("#E6F4EC"), rooms.Cell(2, status).Style.Fill.BackgroundColor); // created, clean
            Assert.Equal(XLColor.FromHtml("#FDF1E1"), rooms.Cell(3, status).Style.Fill.BackgroundColor); // created, repaired
            Assert.Equal(XLColor.FromHtml("#FBE8E7"), rooms.Cell(4, status).Style.Fill.BackgroundColor); // skipped
            Assert.True(rooms.Cell(4, 10).IsEmpty()); // no roof for the skipped room
        }

        [Fact]
        public void MissingFolderIsCreated()
        {
            string deep = Path.Combine(_folder, "a", "b");
            Assert.True(File.Exists(ReportWriter.Write(Build(), deep)));
        }

        [Fact]
        public void LockedFileGetsSuffix()
        {
            var report = Build();
            string first = ReportWriter.Write(report, _folder);
            using (new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                string second = ReportWriter.Write(report, _folder);
                Assert.NotEqual(first, second);
                Assert.EndsWith("_1.xlsx", second);
                Assert.True(File.Exists(second));
            }
        }

        [Fact]
        public void EmptyReportStillWrites()
        {
            var report = RunReport.Build(new ReportSummary { RunId = "r", Started = DateTime.Now, Model = "" }, null, null, null, null, null, null);
            Assert.True(File.Exists(ReportWriter.Write(report, _folder)));
        }
    }
}
