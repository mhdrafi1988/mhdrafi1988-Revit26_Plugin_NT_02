using System.Collections.Generic;
using ClosedXML.Excel;
using Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Services
{
    public static class FloorTypeImportService
    {
        // Col layout (1-indexed):
        // 1  Type Name        (blank = continuation of previous type)
        // 2  Type Mark
        // 3  Function
        // 4  Total Thickness (mm)
        // 5  Wraps At Inserts
        // 6  Wraps At Ends
        // 7  Layer #
        // 8  Material
        // 9  Thickness (mm)
        // 10 Layer Function
        // 11 Priority
        // 12 Variable

        public static List<ImportPreviewItem> LoadPreview(
            string filePath,
            System.Collections.Generic.HashSet<string> existingNames)
        {
            var result  = new List<ImportPreviewItem>();
            ImportPreviewItem current = null;

            using (var wb = new XLWorkbook(filePath))
            {
                foreach (var ws in wb.Worksheets)
                {
                    if (ws.Cell(1, 1).GetString() != "Type Name") continue;

                    int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

                    for (int r = 2; r <= lastRow; r++)
                    {
                        string typeName = ws.Cell(r, 1).GetString();

                        // New type block when Type Name cell is non-empty
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            // Finalise the previous item
                            if (current != null)
                                FinaliseItem(current);

                            current = new ImportPreviewItem
                            {
                                SheetName        = ws.Name,
                                TypeName         = typeName,
                                TotalThicknessMm = ws.Cell(r, 4).GetValue<double>(),
                                WrapsAtInserts   = NullCoalesce(ws.Cell(r, 5).GetString(), "NoInsertWrap"),
                                WrapsAtEnds      = NullCoalesce(ws.Cell(r, 6).GetString(), "NoWrap"),
                                Status           = existingNames.Contains(typeName)
                                                   ? ImportStatus.Dup : ImportStatus.New,
                                Layers           = new System.Collections.Generic.List<LayerData>()
                            };
                            result.Add(current);
                        }

                        // Skip orphan layer rows (no type started yet)
                        if (current == null) continue;

                        string mat      = ws.Cell(r, 8).GetString();
                        string layerFunc = ws.Cell(r, 10).GetString();

                        // Skip blank layer rows
                        if (string.IsNullOrWhiteSpace(mat) && string.IsNullOrWhiteSpace(layerFunc))
                            continue;

                        string varCell = ws.Cell(r, 12).GetString();
                        int    pri     = ws.Cell(r, 11).GetValue<int>();

                        current.Layers.Add(new LayerData
                        {
                            MaterialName  = mat,
                            ThicknessMm   = ws.Cell(r, 9).GetValue<double>(),
                            LayerFunction = layerFunc,
                            Priority      = pri > 0 ? pri : 1,
                            IsVariable    = varCell.Equals("Yes",  System.StringComparison.OrdinalIgnoreCase)
                                         || varCell == "1"
                                         || varCell.Equals("True", System.StringComparison.OrdinalIgnoreCase)
                        });
                    }

                    // Finalise the last item on this sheet
                    if (current != null)
                        FinaliseItem(current);
                    current = null;
                }
            }

            return result;
        }

        private static void FinaliseItem(ImportPreviewItem item)
        {
            item.LayerCount = item.Layers.Count;
            if (item.TotalThicknessMm == 0)
            {
                double sum = 0;
                foreach (var l in item.Layers) sum += l.ThicknessMm;
                item.TotalThicknessMm = sum;
            }
        }

        private static string NullCoalesce(string value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
