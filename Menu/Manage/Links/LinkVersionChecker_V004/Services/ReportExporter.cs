using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ClosedXML.Excel;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Services
{
    /// <summary>
    /// Formatted .xlsx (ClosedXML) with Summary / Links / Findings sheets.
    /// If ClosedXML can't load (e.g. another add-in loaded a different OpenXML version),
    /// it falls back to a CSV next to the chosen path.
    /// </summary>
    public static class ReportExporter
    {
        /// <returns>The path actually written.</returns>
        public static string Export(string xlsxPath, IList<LinkInfo> links, Func<string, LoadTimeEntry> hostTime)
        {
            try
            {
                ExportXlsx(xlsxPath, links, hostTime);
                return xlsxPath;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is FileLoadException || ex is TypeLoadException || ex is MissingMethodException)
            {
                string csv = Path.ChangeExtension(xlsxPath, ".csv");
                ExportCsv(csv, links);
                return csv;
            }
        }

        // Kept in its own method so a missing ClosedXML only fails here, not in Export().
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExportXlsx(string path, IList<LinkInfo> links, Func<string, LoadTimeEntry> hostTime)
        {
            using var wb = new XLWorkbook();

            // ---------------- Summary ----------------
            var s = wb.Worksheets.Add("Summary");
            string[] sh = { "Host", "Links", "From ACC", "On disk", "Critical", "Warning", "Temporary upgrades", "IFC cache issues", "Not found",
                            "Est. extra load (s)", "Last measured open (s)", "Measured on" };
            for (int c = 0; c < sh.Length; c++) s.Cell(1, c + 1).Value = sh[c];
            int r = 2;
            foreach (var g in links.GroupBy(l => l.HostPath ?? "").OrderBy(g => g.Key))
            {
                var list = g.ToList();
                var ht = hostTime?.Invoke(g.Key);
                s.Cell(r, 1).Value = list[0].HostName;
                s.Cell(r, 2).Value = list.Count;
                s.Cell(r, 3).Value = list.Count(l => LinkSourceInfo.IsAcc(l.Source));
                s.Cell(r, 4).Value = list.Count(l => LinkSourceInfo.Group(l.Source) == SourceGroup.Disk);
                s.Cell(r, 5).Value = list.Count(l => l.Severity == Severity.Critical);
                s.Cell(r, 6).Value = list.Count(l => l.Severity == Severity.Warning);
                s.Cell(r, 7).Value = list.Count(l => l.Findings.Any(f => f.RuleId == "TEMP_UPGRADE"));
                s.Cell(r, 8).Value = list.Count(l => l.Findings.Any(f => f.RuleId == "IFC_CACHE"));
                s.Cell(r, 9).Value = list.Count(l => l.Findings.Any(f => f.RuleId == "MISSING"));
                s.Cell(r, 10).Value = list.Where(l => l.CountsInTotal).Sum(l => l.EstSeconds);
                if (ht != null)
                {
                    s.Cell(r, 11).Value = Math.Round(ht.Seconds, 1);
                    s.Cell(r, 12).Value = ht.At;
                    s.Cell(r, 12).Style.DateFormat.Format = "dd-mmm-yy hh:mm";
                }
                r++;
            }
            StyleHeader(s, sh.Length);
            s.Columns().AdjustToContents();

            // ---------------- Links ----------------
            var w = wb.Worksheets.Add("Links");
            string[] lh = { "Host", "Name", "Type", "Source", "Source Group", "Nested", "Parent", "Saved In", "Check", "Severity", "Workshared",
                            "Size (MB)", "Path Type", "Status", "Attachment", "Instances", "Worksets",
                            "Est. Extra Load (s)", "Counts In Total", "Measured (s)", "IFC Cache", "Full Path", "Recommendation" };
            for (int c = 0; c < lh.Length; c++) w.Cell(1, c + 1).Value = lh[c];
            r = 2;
            foreach (var l in links)
            {
                object[] row =
                {
                    l.HostName, l.Name, l.KindText, l.SourceText, l.SourceGroupText, l.IsNested ? "Yes" : "No", l.ParentName, l.SavedInText, l.CheckText,
                    l.Severity.ToString(), l.WorksharedText, Math.Round(l.SizeMb, 1), l.PathText, l.Status, l.Attachment,
                    l.Instances < 0 ? "" : l.Instances.ToString(), l.WorksetsText, l.EstSeconds, l.CountsInTotal ? "Yes" : "No",
                    l.MeasuredSeconds.HasValue ? Math.Round(l.MeasuredSeconds.Value, 1).ToString("0.0") : "",
                    l.CacheText, string.IsNullOrEmpty(l.InSessionPath) ? l.FullPath : l.InSessionPath, l.Recommendation.Replace("• ", "")
                };
                for (int c = 0; c < row.Length; c++) SetCell(w.Cell(r, c + 1), row[c]);

                string color = l.Severity == Severity.Critical ? "#FDECEA" : l.Severity == Severity.Warning ? "#FFF4E0" : null;
                if (color != null) w.Range(r, 1, r, lh.Length).Style.Fill.BackgroundColor = XLColor.FromHtml(color);
                r++;
            }
            StyleHeader(w, lh.Length);
            w.Columns().AdjustToContents();
            w.Column(lh.Length).Width = 80;
            w.Column(lh.Length).Style.Alignment.WrapText = true;
            w.Column(lh.Length - 1).Width = 60;

            // ---------------- Findings ----------------
            var f = wb.Worksheets.Add("Findings");
            string[] fh = { "Host", "Link", "Rule", "Severity", "Title", "Est. (s)", "Message" };
            for (int c = 0; c < fh.Length; c++) f.Cell(1, c + 1).Value = fh[c];
            r = 2;
            foreach (var l in links)
                foreach (var fd in l.Findings.OrderByDescending(x => x.Severity))
                {
                    f.Cell(r, 1).Value = l.HostName;
                    f.Cell(r, 2).Value = l.Name;
                    f.Cell(r, 3).Value = fd.RuleId;
                    f.Cell(r, 4).Value = fd.Severity.ToString();
                    f.Cell(r, 5).Value = fd.Title;
                    f.Cell(r, 6).Value = fd.EstSeconds;
                    f.Cell(r, 7).Value = fd.Message;
                    r++;
                }
            StyleHeader(f, fh.Length);
            f.Columns().AdjustToContents();
            f.Column(7).Width = 100;
            f.Column(7).Style.Alignment.WrapText = true;

            wb.SaveAs(path);
        }

        private static void SetCell(IXLCell cell, object v)
        {
            switch (v)
            {
                case double d: cell.Value = d; break;
                case int i: cell.Value = i; break;
                default: cell.Value = v?.ToString() ?? ""; break;
            }
        }

        private static void StyleHeader(IXLWorksheet ws, int cols)
        {
            var hdr = ws.Range(1, 1, 1, cols);
            hdr.Style.Font.Bold = true;
            hdr.Style.Font.FontColor = XLColor.White;
            hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2A37");
            ws.SheetView.FreezeRows(1);
            int last = Math.Max(ws.LastRowUsed()?.RowNumber() ?? 1, 1);
            ws.Range(1, 1, last, cols).SetAutoFilter();
        }

        public static void ExportCsv(string path, IEnumerable<LinkInfo> links)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Row("Host", "Name", "Type", "Source", "Source Group", "Nested", "Parent", "Saved In", "Check", "Severity", "Workshared", "Size (MB)",
                              "Path Type", "Status", "Attachment", "Instances", "Worksets", "Est. Extra Load (s)", "Counts In Total",
                              "Measured (s)", "IFC Cache", "Full Path", "Recommendation"));
            foreach (var l in links)
                sb.AppendLine(Row(l.HostName, l.Name, l.KindText, l.SourceText, l.SourceGroupText, l.IsNested ? "Yes" : "No", l.ParentName, l.SavedInText, l.CheckText,
                                  l.Severity.ToString(), l.WorksharedText, l.SizeMb.ToString("0.0"), l.PathText, l.Status, l.Attachment,
                                  l.InstancesText, l.WorksetsText, l.EstSeconds.ToString(), l.CountsInTotal ? "Yes" : "No",
                                  l.MeasuredSeconds?.ToString("0.0") ?? "", l.CacheText, l.FullPath, l.Recommendation.Replace("• ", "")));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Row(params string[] cells) =>
            string.Join(",", cells.Select(c => "\"" + (c ?? "").Replace("\"", "\"\"") + "\""));
    }
}
