using System.Collections.Generic;
using ClosedXML.Excel;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Core.Services
{
    /// <summary>Writes the link status grid to an .xlsx file.</summary>
    public static class LinkStatusExporter
    {
        /// <summary>Report format version, written to the "Info" sheet. Bump when columns change (append only).</summary>
        public const int FormatVersion = 2;   // 2: appended "Source" column

        /// <summary>Column headers, in order. New columns go at the end.</summary>
        public static readonly string[] Columns =
        {
            "Link name", "In model", "On this PC (Desktop Connector)", "Latest from ACC?", "Folder in ACC",
            "Local file date", "Size (MB)", "Instances", "Edited by", "Full path", "Note", "Source"
        };

        /// <summary>Saves <paramref name="rows"/> to <paramref name="path"/>. Throws on I/O failure (caller reports it).</summary>
        public static void Save(string path, string hostName, IEnumerable<LinkStatusRow> rows)
        {
            using var wb = new XLWorkbook();
            IXLWorksheet ws = wb.Worksheets.Add("Links");

            for (int c = 0; c < Columns.Length; c++) ws.Cell(1, c + 1).Value = Columns[c];
            ws.Row(1).Style.Font.Bold = true;

            int r = 2;
            foreach (LinkStatusRow row in rows)
            {
                ws.Cell(r, 1).Value = row.Name;
                ws.Cell(r, 2).Value = row.ModelText;
                ws.Cell(r, 3).Value = row.DcText;
                ws.Cell(r, 4).Value = row.LatestText;
                ws.Cell(r, 5).Value = row.AccFolder;
                if (row.FileDate.HasValue) ws.Cell(r, 6).Value = row.FileDate.Value;
                if (row.SizeMb.HasValue) ws.Cell(r, 7).Value = System.Math.Round(row.SizeMb.Value, 1);
                ws.Cell(r, 8).Value = row.Instances;
                ws.Cell(r, 9).Value = row.OwnedBy;
                ws.Cell(r, 10).Value = row.FullPath;
                ws.Cell(r, 11).Value = row.LatestReason;
                ws.Cell(r, 12).Value = row.SourceText;
                r++;
            }

            ws.Column(6).Style.DateFormat.Format = "dd-mm-yyyy hh:mm";
            ws.Columns().AdjustToContents(1, System.Math.Min(r, 200));
            ws.SheetView.FreezeRows(1);

            IXLWorksheet info = wb.Worksheets.Add("Info");
            info.Cell(1, 1).Value = "Host model";
            info.Cell(1, 2).Value = hostName;
            info.Cell(2, 1).Value = "Report format";
            info.Cell(2, 2).Value = FormatVersion;
            info.Cell(3, 1).Value = "Exported";
            info.Cell(3, 2).Value = System.DateTime.Now;

            wb.SaveAs(path);
        }
    }
}
