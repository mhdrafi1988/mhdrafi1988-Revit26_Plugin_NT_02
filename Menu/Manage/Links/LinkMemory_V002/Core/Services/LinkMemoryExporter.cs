using ClosedXML.Excel;
using Revit26_Plugin_Revised.LinkMemory.V002.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Core.Services
{
    /// <summary>
    /// Writes a memory to an .xlsx file with four sheets: Links, Views, Link × View and Info.
    /// Columns never change order; new ones go at the end and bump <see cref="FormatVersion"/>.
    /// </summary>
    public static class LinkMemoryExporter
    {
        /// <summary>Report format version, written to the Info sheet.</summary>
        public const int FormatVersion = 1;

        /// <summary>Links sheet columns, in order.</summary>
        public static readonly string[] LinkColumns =
            { "Link name", "Type", "Path", "Status now", "In memory", "Visible in views", "Workset" };

        /// <summary>Views sheet columns, in order.</summary>
        public static readonly string[] ViewColumns =
            { "View name", "View type", "Sheet number", "Sheet name", "Why collected", "Links visible" };

        /// <summary>Saves <paramref name="memory"/> to <paramref name="path"/>. Throws on I/O failure (caller reports it).</summary>
        public static void Save(string path, string modelName, LinkMemoryItem memory, IList<LiveLink> live)
        {
            using var wb = new XLWorkbook();
            var names = memory.Links.Where(l => l.UniqueId != null).GroupBy(l => l.UniqueId)
                .ToDictionary(g => g.Key, g => g.First().Name);

            // ── Links ──
            IXLWorksheet links = wb.Worksheets.Add("Links");
            Header(links, LinkColumns);
            int r = 2;
            foreach (SavedLink l in memory.Links.OrderBy(l => l.Kind).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            {
                LiveLink now = LinkCatalog.Match(live, l);
                links.Cell(r, 1).Value = l.Name;
                links.Cell(r, 2).Value = LinkCatalog.KindText(l.Kind);
                links.Cell(r, 3).Value = now?.Path ?? l.Path ?? string.Empty;
                links.Cell(r, 4).Value = now == null ? "Not in model" : StatusText(now);
                links.Cell(r, 5).Value = UseText(l.Use);
                links.Cell(r, 6).Value = l.ViewCount;
                links.Cell(r, 7).Value = now?.Workset ?? string.Empty;
                r++;
            }
            Finish(links, r);

            // ── Views ──
            IXLWorksheet views = wb.Worksheets.Add("Views");
            Header(views, ViewColumns);
            r = 2;
            foreach (SavedView v in memory.Views)
            {
                views.Cell(r, 1).Value = v.Name;
                views.Cell(r, 2).Value = v.ViewType;
                views.Cell(r, 3).Value = v.SheetNumber ?? string.Empty;
                views.Cell(r, 4).Value = v.SheetName ?? string.Empty;
                views.Cell(r, 5).Value = v.Reason;
                views.Cell(r, 6).Value = string.Join("; ", v.LinkIds.Select(id => names.TryGetValue(id, out string n) ? n : id));
                r++;
            }
            Finish(views, r);

            // ── Link × View ──
            IXLWorksheet matrix = wb.Worksheets.Add("Link × View");
            matrix.Cell(1, 1).Value = "Link \\ View";
            List<SavedView> cols = memory.Views.Take(16000).ToList();
            for (int c = 0; c < cols.Count; c++) matrix.Cell(1, c + 2).Value = cols[c].Name;
            matrix.Row(1).Style.Font.Bold = true;
            r = 2;
            foreach (SavedLink l in memory.Links.Where(l => l.ViewCount > 0).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            {
                matrix.Cell(r, 1).Value = l.Name;
                for (int c = 0; c < cols.Count; c++)
                    if (cols[c].LinkIds.Contains(l.UniqueId)) matrix.Cell(r, c + 2).Value = "●";
                r++;
            }
            matrix.Column(1).AdjustToContents();
            matrix.SheetView.FreezeRows(1);
            matrix.SheetView.FreezeColumns(1);

            // ── Info ──
            IXLWorksheet info = wb.Worksheets.Add("Info");
            Header(info, new[] { "Field", "Value" });
            var rows = new (string, object)[]
            {
                ("Format version", FormatVersion),
                ("Model", modelName ?? string.Empty),
                ("Memory", memory.Name ?? string.Empty),
                ("Scope", ScopeText(memory.Scope)),
                ("Source", memory.SourceName ?? string.Empty),
                ("Collected", memory.SavedAt),
                ("Exported", DateTime.Now),
                ("Tool", ToolCatalog.LinkMemoryV002.Title)
            };
            for (int i = 0; i < rows.Length; i++)
            {
                info.Cell(i + 2, 1).Value = rows[i].Item1;
                info.Cell(i + 2, 2).Value = XLCellValue.FromObject(rows[i].Item2);
                if (rows[i].Item2 is DateTime) info.Cell(i + 2, 2).Style.DateFormat.Format = "dd-mm-yyyy hh:mm";
            }
            info.Columns().AdjustToContents();

            wb.SaveAs(path);
        }

        /// <summary>Status text of a live link.</summary>
        public static string StatusText(LiveLink l)
        {
            if (l.Kind == LinkKind.CadImport) return "Imported";
            if (l.Status == null) return l.IsLoaded ? "Loaded" : "Unloaded";
            return l.Status switch
            {
                Autodesk.Revit.DB.LinkedFileStatus.Loaded => "Loaded",
                Autodesk.Revit.DB.LinkedFileStatus.NotFound => "Not found",
                Autodesk.Revit.DB.LinkedFileStatus.InClosedWorkset => "Closed workset",
                Autodesk.Revit.DB.LinkedFileStatus.LocallyUnloaded => "Unloaded (me)",
                Autodesk.Revit.DB.LinkedFileStatus.Invalid => "Invalid",
                _ => "Unloaded"
            };
        }

        /// <summary>Text of <paramref name="use"/>.</summary>
        public static string UseText(LinkUse use) => use switch
        {
            LinkUse.Used => "Used",
            LinkUse.NotUsed => "Not used",
            _ => "Unknown (was unloaded)"
        };

        /// <summary>Text of <paramref name="scope"/>.</summary>
        public static string ScopeText(MemoryScope scope) => scope switch
        {
            MemoryScope.ActiveView => "View + related",
            MemoryScope.PrintSet => "Print set",
            _ => "Whole model"
        };

        private static void Header(IXLWorksheet ws, string[] columns)
        {
            for (int c = 0; c < columns.Length; c++) ws.Cell(1, c + 1).Value = columns[c];
            ws.Row(1).Style.Font.Bold = true;
        }

        private static void Finish(IXLWorksheet ws, int lastRow)
        {
            ws.Columns().AdjustToContents(1, Math.Min(lastRow, 200));
            ws.SheetView.FreezeRows(1);
        }
    }
}
