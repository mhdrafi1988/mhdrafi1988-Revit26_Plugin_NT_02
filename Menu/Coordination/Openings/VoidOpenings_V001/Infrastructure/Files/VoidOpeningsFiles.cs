using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClosedXML.Excel;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.Reporting;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Files
{
    /// <summary>
    /// File access of the tool: the settings file under %AppData%\Revit26_Plugin_Revised\VoidOpenings,
    /// opening packages, and the Excel and JSON exports of the results.
    /// </summary>
    public sealed class VoidOpeningsFiles
    {
        /// <summary>Full path of the settings file.</summary>
        public string SettingsPath { get; } = JsonFileStore.PathFor("VoidOpenings", "settings.json");

        /// <summary>
        /// Loads the settings; the defaults when there is no file yet. <paramref name="warning"/> says so
        /// when an existing file could not be read.
        /// </summary>
        public VoidOpeningsSettings LoadSettings(out string warning)
        {
            warning = string.Empty;
            if (!File.Exists(SettingsPath))
                return VoidOpeningsSettings.CreateDefault();
            try
            {
                return SettingsSerializer.FromJson(File.ReadAllText(SettingsPath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                warning = $"The saved settings could not be read ({ex.Message}); the default clearances are shown.";
                return VoidOpeningsSettings.CreateDefault();
            }
        }

        /// <summary>Saves the settings.</summary>
        public void SaveSettings(VoidOpeningsSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, SettingsSerializer.ToJson(settings));
        }

        /// <summary>Writes an opening package.</summary>
        public void WritePackage(string path, OpeningPackage package) => File.WriteAllText(path, PackageSerializer.ToJson(package));

        /// <summary>Reads an opening package. Throws <see cref="InvalidOperationException"/> with a message for the user when the file is not one.</summary>
        public OpeningPackage ReadPackage(string path) => PackageSerializer.FromJson(File.ReadAllText(path));

        /// <summary>Writes the result rows as JSON, with a format version.</summary>
        public void WriteResultsJson(string path, IReadOnlyList<ExportRow> rows)
        {
            var document = new { Version = 1, CreatedUtc = DateTime.UtcNow.ToString("o"), Openings = rows };
            File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Writes the result rows as an Excel workbook: one sheet, the results-table columns first.</summary>
        public void WriteResultsExcel(string path, IReadOnlyList<ExportRow> rows)
        {
            using (var workbook = new XLWorkbook())
            {
                IXLWorksheet sheet = workbook.Worksheets.Add("Void Openings");
                for (int c = 0; c < ExportRow.Headers.Count; c++)
                    sheet.Cell(1, c + 1).Value = ExportRow.Headers[c];
                sheet.Row(1).Style.Font.Bold = true;

                for (int r = 0; r < rows.Count; r++)
                {
                    object[] values = rows[r].ToValues();
                    for (int c = 0; c < values.Length; c++)
                    {
                        IXLCell cell = sheet.Cell(r + 2, c + 1);
                        switch (values[c])
                        {
                            case double number: cell.Value = number; break;
                            case long whole: cell.Value = whole; break;
                            default: cell.Value = values[c]?.ToString() ?? string.Empty; break;
                        }
                    }
                }

                sheet.SheetView.FreezeRows(1);
                sheet.Columns().AdjustToContents();
                workbook.SaveAs(path);
            }
        }
    }
}
