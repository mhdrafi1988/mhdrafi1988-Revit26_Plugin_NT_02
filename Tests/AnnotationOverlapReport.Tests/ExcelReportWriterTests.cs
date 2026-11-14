using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Infrastructure;
using Xunit;

namespace AnnotationOverlapReport.Tests
{
    public class ExcelReportWriterTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "aor_" + Guid.NewGuid().ToString("N") + ".xlsx");

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private static ReportRow Row(long a, long b, double mm, string textA = "204A") => new ReportRow
        {
            ViewId = 10, Sheet = "A-201", View = "L02 Floor Plan", Scale = "1:100",
            TypeA = "Door Tags", TextA = textA, IdA = a, TypeB = "Room Tags", TextB = "2.14 Office", IdB = b, OverlapMm = mm
        };

        private static ReportRunInfo Info() => new ReportRunInfo
        {
            ToolVersion = "1.0.0", RunAt = new DateTime(2026, 11, 14, 9, 0, 0), ViewsScanned = 4,
            AnnotationsScanned = 859, NotMeasured = 2, SkippedViews = new List<string> { "3D View 1: not a 2D view" }
        };

        [Fact]
        public void Header_row_has_the_ten_columns_in_order()
        {
            ExcelReportWriter.Write(_path, new[] { Row(1, 2, 1.8) }, Info());
            using var wb = new XLWorkbook(_path);
            var header = wb.Worksheet("Overlaps").Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
            Assert.Equal(new[] { "Sheet", "View", "Scale", "Type A", "Text A", "ID A", "Type B", "Text B", "ID B", "Overlap (mm)" }, header);
            Assert.Equal(header, ExcelReportWriter.Columns);
        }

        [Fact]
        public void One_row_per_pair()
        {
            ExcelReportWriter.Write(_path, new[] { Row(1, 2, 1.8), Row(3, 4, 0.7), Row(5, 6, 2.3) }, Info());
            using var wb = new XLWorkbook(_path);
            var ws = wb.Worksheet("Overlaps");
            Assert.Equal(4, ws.LastRowUsed().RowNumber());
            Assert.Equal("A-201", ws.Cell(2, 1).GetString());
            Assert.Equal(1, ws.Cell(2, 6).GetValue<long>());
            Assert.Equal(2.3, ws.Cell(4, 10).GetValue<double>(), 6);
        }

        [Fact]
        public void Text_is_written_as_plain_single_line_string()
        {
            ExcelReportWriter.Write(_path, new[] { Row(1, 2, 1.8, "=SUM(1)\r\nLINE  two") }, Info());
            using var wb = new XLWorkbook(_path);
            var cell = wb.Worksheet("Overlaps").Cell(2, 5);
            Assert.False(cell.HasFormula);
            Assert.Equal("=SUM(1) LINE two", cell.GetString());
        }

        [Fact]
        public void Clean_handles_null_and_whitespace()
        {
            Assert.Equal("", ExcelReportWriter.Clean(null));
            Assert.Equal("a b", ExcelReportWriter.Clean("  a\n\tb  "));
        }

        [Fact]
        public void Run_info_has_format_version_1()
        {
            ExcelReportWriter.Write(_path, new ReportRow[0], Info());
            using var wb = new XLWorkbook(_path);
            var ws = wb.Worksheet("Run info");
            var map = ws.RowsUsed().ToDictionary(r => r.Cell(1).GetString(), r => r.Cell(2).GetString());
            Assert.Equal("1", map["Format version"]);
            Assert.Equal("1.0.0", map["Tool version"]);
            Assert.Equal("4", map["Views scanned"]);
            Assert.Equal("3D View 1: not a 2D view", map["Skipped view"]);
        }
    }
}
