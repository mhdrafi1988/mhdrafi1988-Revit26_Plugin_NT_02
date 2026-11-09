using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofTypeCreator.V003.Core.Models;
using Revit26_Plugin_Revised.RoofTypeCreator.V003.Core.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofTypeCreator.V003.Infrastructure.ExternalEvents
{
    public class RoofTypeExternalEventHandler : IExternalEventHandler
    {
        // ── Request and payloads set before Raise() ──────────────
        public RoofTypeRequest    PendingRequest  { get; set; } = RoofTypeRequest.None;
        public List<RoofTypeItem> ItemsToExport   { get; set; }
        public List<RoofTypeItem> LoadedItems     { get; set; }   // all types currently in model
        public string             ExportFilePath  { get; set; }
        public string             ImportFilePath  { get; set; }
        public List<ImportPreviewItem> ItemsToImport    { get; set; }
        public string                  TemplateFilePath { get; set; }

        // ── Callbacks invoked from Execute() ─────────────────────
        public Action<List<RoofTypeItem>>      OnTypesLoaded      { get; set; }
        public Action<string>                  OnExportDone       { get; set; }
        public Action<List<ImportPreviewItem>> OnPreviewLoaded    { get; set; }
        public Action<ImportResult>            OnImportDone       { get; set; }
        public Action<string>                  OnTemplateDone     { get; set; }
        public Action<string, string>          OnLog              { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            switch (PendingRequest)
            {
                case RoofTypeRequest.LoadTypes:        ExecuteLoad(doc);             break;
                case RoofTypeRequest.ExportSelected:   ExecuteExport(doc);           break;
                case RoofTypeRequest.PreviewImport:    ExecutePreview(doc);          break;
                case RoofTypeRequest.ImportNew:        ExecuteImport(doc);           break;
                case RoofTypeRequest.DownloadTemplate: ExecuteDownloadTemplate(doc); break;
            }

            PendingRequest = RoofTypeRequest.None;
        }

        public string GetName() => "Roof Type Manager";

        // ── Handlers ─────────────────────────────────────────────

        private void ExecuteLoad(Document doc)
        {
            try
            {
                Log("Info", "Loading roof types from model…");
                var items = RoofTypeDataService.LoadFromDocument(doc);
                OnTypesLoaded?.Invoke(items);
                Log("Success", $"{items.Count} roof types loaded.");
            }
            catch (Exception ex) { Log("Error", $"Load failed: {ex.Message}"); }
        }

        private void ExecuteExport(Document doc)
        {
            try
            {
                if (ItemsToExport == null || ItemsToExport.Count == 0)
                {
                    Log("Warning", "No types selected for export.");
                    OnExportDone?.Invoke(null);
                    return;
                }
                Log("Info", $"Exporting {ItemsToExport.Count} roof types to {Path.GetFileName(ExportFilePath)}…");
                RoofTypeExportService.Export(ItemsToExport, ExportFilePath, doc);
                Log("Success", $"Exported {ItemsToExport.Count} types → {Path.GetFileName(ExportFilePath)}");
                OnExportDone?.Invoke(ExportFilePath);
            }
            catch (Exception ex)
            {
                Log("Error", $"Export failed: {ex.Message}");
                OnExportDone?.Invoke(null);
            }
        }

        private void ExecutePreview(Document doc)
        {
            try
            {
                Log("Info", $"Loading import preview: {Path.GetFileName(ImportFilePath)}…");
                var existing = RoofTypeDataService.GetExistingTypeNames(doc);
                var items    = RoofTypeImportService.LoadPreview(ImportFilePath, existing);
                OnPreviewLoaded?.Invoke(items);
                int newCount = items.Count(i => i.Status == ImportStatus.New);
                int dupCount = items.Count(i => i.Status == ImportStatus.Dup);
                Log("Info", $"{items.Count} types previewed — {newCount} new, {dupCount} dup.");
            }
            catch (Exception ex)
            {
                Log("Error", $"Preview failed: {ex.Message}");
                OnPreviewLoaded?.Invoke(new List<ImportPreviewItem>());
            }
        }

        private void ExecuteImport(Document doc)
        {
            var result = new ImportResult();
            // Esc / Cancel with a Yes-No confirm; the import transaction below is rolled back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.RoofTypeManager, null);
            try
            {
                var allItems = ItemsToImport ?? new List<ImportPreviewItem>();
                if (allItems.Count == 0)
                {
                    Log("Warning", "No types to import.");
                    OnImportDone?.Invoke(result);
                    return;
                }

                var templateType = new FilteredElementCollector(doc)
                    .OfClass(typeof(RoofType))
                    .Cast<RoofType>()
                    .FirstOrDefault();

                if (templateType == null)
                {
                    Log("Error", "No existing roof type found to use as a duplication template.");
                    OnImportDone?.Invoke(result);
                    return;
                }

                // Build set of all existing names so we can find unique suffixes for Dup items
                var existingNames = new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofType))
                        .Cast<RoofType>()
                        .Select(t => t.Name),
                    StringComparer.OrdinalIgnoreCase);

                using (var tx = new Transaction(doc, "Import Roof Types"))
                {
                    tx.Start();
                    foreach (var item in allItems)
                    {
                        ToolCancellation.CheckCurrent();
                        // Resolve name: New → use as-is; Dup/Renamed → find unique suffix
                        string targetName = item.TypeName;
                        if (item.Status == ImportStatus.Dup || item.Status == ImportStatus.Renamed)
                        {
                            int suffix = 2;
                            while (existingNames.Contains($"{item.TypeName} ({suffix})")) suffix++;
                            targetName = $"{item.TypeName} ({suffix})";
                            result.Renamed++;
                            Log("Info", $"Renamed '{item.TypeName}' → '{targetName}'");
                        }

                        try
                        {
                            var newType = templateType.Duplicate(targetName) as RoofType;
                            if (newType == null) { result.Failed++; continue; }
                            existingNames.Add(targetName);

                            if (item.Layers != null && item.Layers.Count > 0)
                            {
                                var (layers, varIdx) = BuildLayers(doc, item.Layers);
                                if (layers != null && layers.Count > 0)
                                {
                                    var cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                                    if (cs != null)
                                    {
                                        if (varIdx >= 0) cs.VariableLayerIndex = varIdx;
                                        if (Enum.TryParse<OpeningWrappingCondition>(item.WrapsAtInserts, out var ow))
                                            cs.OpeningWrapping = ow;
                                        if (Enum.TryParse<EndCapCondition>(item.WrapsAtEnds, out var ew))
                                            cs.EndCap = ew;
                                        for (int li = 0; li < cs.LayerCount && li < item.Layers.Count; li++)
                                        {
                                            int pr = item.Layers[li].Priority;
                                            if (pr > 0) cs.SetLayerPriority(li, pr);
                                        }
                                        newType.SetCompoundStructure(cs);
                                    }
                                }
                            }

                            result.Created++;
                            Log("Success", $"Created: {item.TypeName}");
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            Log("Warning", $"Failed '{item.TypeName}': {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                Log("Info", $"{result.Created} created | {result.Renamed} renamed | {result.Failed} failed");
            }
            catch (OperationCanceledException)
            {
                Log("Warning", "Cancelled by user. No roof types were created.");
                OnImportDone?.Invoke(new ImportResult());
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex) { Log("Error", $"Import failed: {ex.Message}"); }

            OnImportDone?.Invoke(result);
        }

        private void ExecuteDownloadTemplate(Document doc)
        {
            try
            {
                Log("Info", $"Saving template → {Path.GetFileName(TemplateFilePath)}…");

                var items = LoadedItems;
                if (items != null && items.Count > 0)
                {
                    // Pre-fill with actual model layer data
                    RoofTypeExportService.Export(items, TemplateFilePath, doc);
                    Log("Success", $"Template saved with {items.Count} types → {Path.GetFileName(TemplateFilePath)}");
                }
                else
                {
                    // Fallback: blank example template when no types loaded yet
                    RoofTypeExportService.ExportBlankTemplate(TemplateFilePath);
                    Log("Success", $"Blank template saved → {Path.GetFileName(TemplateFilePath)}");
                }

                OnTemplateDone?.Invoke(TemplateFilePath);
            }
            catch (Exception ex)
            {
                Log("Error", $"Template download failed: {ex.Message}");
                OnTemplateDone?.Invoke(null);
            }
        }

        private (List<CompoundStructureLayer> layers, int variableLayerIndex) BuildLayers(Document doc, List<LayerData> layerData)
        {
            var layers = new List<CompoundStructureLayer>();
            bool hasStructure = false;
            int varIdx = -1;

            for (int i = 0; i < layerData.Count; i++)
            {
                var ld = layerData[i];

                double thick = ld.ThicknessMm > 0
                    ? UnitUtils.ConvertToInternalUnits(ld.ThicknessMm, UnitTypeId.Millimeters)
                    : UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);

                var mat = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => string.Equals(m.Name, ld.MaterialName, StringComparison.OrdinalIgnoreCase));

                var func = MaterialFunctionAssignment.Structure;
                if (!string.IsNullOrEmpty(ld.LayerFunction) &&
                    Enum.TryParse<MaterialFunctionAssignment>(ld.LayerFunction, out var parsed))
                    func = parsed;

                if (func == MaterialFunctionAssignment.Structure) hasStructure = true;
                if (ld.IsVariable) varIdx = i;

                var layer = new CompoundStructureLayer(thick, func, mat?.Id ?? ElementId.InvalidElementId);
                layers.Add(layer);
            }

            if (layers.Count > 0 && !hasStructure)
                throw new InvalidOperationException(
                    "No layer with Function=Structure found. Revit requires at least one Structure layer.");

            return (layers, varIdx);
        }

        private void Log(string level, string msg) => OnLog?.Invoke(level, msg);
    }

    public class ImportResult
    {
        public int Created    { get; set; }
        public int Renamed    { get; set; }
        public int Failed     { get; set; }
    }
}
