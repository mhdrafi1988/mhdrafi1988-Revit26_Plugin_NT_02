using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Models;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Services
{
    public static class FloorTypeExportService
    {
        public static void ExportBlankTemplate(string filePath)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Floor Types");
                WriteHeaders(ws);

                // Example type 1 — 3 layers
                int tplRow = 2;
                WriteTypeRows(ws, ref tplRow,
                    "Warm Flat Floor", "RT-01", "Exterior", 310,
                    "NoInsertWrap", "NoWrap",
                    new[] {
                        ("Paving Slab",      50.0,  "Finish1",    1, false),
                        ("Rigid Insulation", 200.0, "Insulation", 1, false),
                        ("Concrete",         60.0,  "Structure",  1, true)
                    });

                // Example type 2 — 4 layers
                WriteTypeRows(ws, ref tplRow,
                    "Composite Deck Floor", "RT-02", "Exterior", 395,
                    "NoInsertWrap", "NoWrap",
                    new[] {
                        ("Felt",             5.0,   "Membrane",          1, false),
                        ("Insulation Board", 150.0, "Insulation",        1, false),
                        ("Screed",           40.0,  "SubstrateExterior", 1, false),
                        ("Concrete Deck",    200.0, "Structure",         1, true)
                    });

                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(filePath);
            }
        }

        public static void Export(IEnumerable<FloorTypeItem> items, string filePath, Document doc)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Floor Types");
                WriteHeaders(ws);

                int row = 2;
                foreach (var item in items)
                {
                    var rt = doc.GetElement(item.TypeId) as FloorType;
                    var cs = rt?.GetCompoundStructure();

                    string wrapsInserts = "NoInsertWrap";
                    string wrapsEnds    = "NoWrap";
                    var layers = new List<(string mat, double thick, string func, int priority, bool variable)>();

                    if (cs != null)
                    {
                        wrapsInserts = cs.OpeningWrapping.ToString();
                        wrapsEnds    = cs.EndCap.ToString();
                        int varIdx   = cs.VariableLayerIndex;

                        for (int li = 0; li < cs.LayerCount; li++)
                        {
                            var layer = cs.GetLayers()[li];
                            string mat = layer.MaterialId != ElementId.InvalidElementId
                                ? (doc.GetElement(layer.MaterialId) as Material)?.Name ?? ""
                                : "";
                            double thick = Math.Round(
                                UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Millimeters), 1);
                            layers.Add((mat, thick, layer.Function.ToString(), cs.GetLayerPriority(li), li == varIdx));
                        }
                    }

                    WriteTypeRows(ws, ref row,
                        item.TypeName, item.TypeMark, item.Function, item.TotalThicknessMm,
                        wrapsInserts, wrapsEnds,
                        layers.ToArray());
                }

                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(filePath);
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────

        private static void WriteHeaders(IXLWorksheet ws)
        {
            ws.Cell(1, 1).Value  = "Type Name";
            ws.Cell(1, 2).Value  = "Type Mark";
            ws.Cell(1, 3).Value  = "Function";
            ws.Cell(1, 4).Value  = "Total Thickness (mm)";
            ws.Cell(1, 5).Value  = "Wraps At Inserts";
            ws.Cell(1, 6).Value  = "Wraps At Ends";
            ws.Cell(1, 7).Value  = "Layer #";
            ws.Cell(1, 8).Value  = "Material";
            ws.Cell(1, 9).Value  = "Thickness (mm)";
            ws.Cell(1, 10).Value = "Layer Function";
            ws.Cell(1, 11).Value = "Priority";
            ws.Cell(1, 12).Value = "Variable";

            var hdr = ws.Row(1);
            hdr.Style.Font.Bold            = true;
            hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A5F");
            hdr.Style.Font.FontColor       = XLColor.White;
        }

        private static void WriteTypeRows(
            IXLWorksheet ws, ref int row,
            string typeName, string typeMark, string function, double totalThicknessMm,
            string wrapsInserts, string wrapsEnds,
            (string mat, double thick, string func, int priority, bool variable)[] layers)
        {
            bool isLight = (row % 2 == 0);

            for (int li = 0; li < layers.Length; li++)
            {
                var (mat, thick, func, priority, variable) = layers[li];

                // Type-level columns: only on the first layer row
                if (li == 0)
                {
                    ws.Cell(row, 1).Value = typeName;
                    ws.Cell(row, 2).Value = typeMark;
                    ws.Cell(row, 3).Value = function;
                    ws.Cell(row, 4).Value = totalThicknessMm;
                    ws.Cell(row, 5).Value = wrapsInserts;
                    ws.Cell(row, 6).Value = wrapsEnds;
                }

                ws.Cell(row, 7).Value  = li + 1;
                ws.Cell(row, 8).Value  = mat;
                ws.Cell(row, 9).Value  = thick;
                ws.Cell(row, 10).Value = func;
                ws.Cell(row, 11).Value = priority;
                ws.Cell(row, 12).Value = variable ? "Yes" : "No";

                if (isLight)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0F4F8");

                row++;
            }
        }
    }
}
