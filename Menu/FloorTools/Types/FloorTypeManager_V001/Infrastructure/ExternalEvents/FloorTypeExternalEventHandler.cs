using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Models;
using Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.Infrastructure.ExternalEvents
{
    public class FloorTypeExternalEventHandler : IExternalEventHandler
    {
        // ── Request and payloads set before Raise() ──────────────
        public FloorTypeRequest    PendingRequest  { get; set; } = FloorTypeRequest.None;
        public List<FloorTypeItem> ItemsToExport   { get; set; }
        public List<FloorTypeItem> LoadedItems     { get; set; }   // all types currently in model
        public string             ExportFilePath  { get; set; }
        public string             ImportFilePath  { get; set; }
        public List<ImportPreviewItem> ItemsToImport    { get; set; }
        public string                  TemplateFilePath { get; set; }

        // ── Callbacks invoked from Execute() ─────────────────────
        public Action<List<FloorTypeItem>>      OnTypesLoaded      { get; set; }
        public Action<string>                  OnExportDone       { get; set; }
        public Action<List<ImportPreviewItem>> OnPreviewLoaded    { get; set; }
        public Action<ImportResult>            OnImportDone       { get; set; }
        public Action<string>                  OnTemplateDone     { get; set; }
        public Action<string, string>          OnLog              { get; set; }

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            switch (PendingRequest)
            {
                case FloorTypeRequest.LoadTypes:        ExecuteLoad(doc);             break;
                case FloorTypeRequest.ExportSelected:   ExecuteExport(doc);           break;
                case FloorTypeRequest.PreviewImport:    ExecutePreview(doc);          break;
                case FloorTypeRequest.ImportNew:        ExecuteImport(doc);           break;
                case FloorTypeRequest.DownloadTemplate: ExecuteDownloadTemplate(doc); break;
            }

            PendingRequest = FloorTypeRequest.None;
        }

        public string GetName() => "Floor Type Manager";

        // ── Handlers ─────────────────────────────────────────────

        private void ExecuteLoad(Document doc)
        {
            try
            {
                Log("Info", "Loading floor types from model…");
                var items = FloorTypeDataService.LoadFromDocument(doc);
                OnTypesLoaded?.Invoke(items);
                Log("Success", $"{items.Count} floor types loaded.");
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
                Log("Info", $"Exporting {ItemsToExport.Count} floor types to {Path.GetFileName(ExportFilePath)}…");
                FloorTypeExportService.Export(ItemsToExport, ExportFilePath, doc);
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
                var existing = FloorTypeDataService.GetExistingTypeNames(doc);
                var items    = FloorTypeImportService.LoadPreview(ImportFilePath, existing);
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
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault();

                if (templateType == null)
                {
                    Log("Error", "No existing floor type found to use as a duplication template.");
                    OnImportDone?.Invoke(result);
                    return;
                }

                // Build set of all existing names so we can find unique suffixes for Dup items
                var existingNames = new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(FloorType))
                        .Cast<FloorType>()
                        .Select(t => t.Name),
                    StringComparer.OrdinalIgnoreCase);

                using (var tx = new Transaction(doc, "Import Floor Types"))
                {
                    tx.Start();
                    foreach (var item in allItems)
                    {
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
                            var newType = templateType.Duplicate(targetName) as FloorType;
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
                    FloorTypeExportService.Export(items, TemplateFilePath, doc);
                    Log("Success", $"Template saved with {items.Count} types → {Path.GetFileName(TemplateFilePath)}");
                }
                else
                {
                    // Fallback: blank example template when no types loaded yet
                    FloorTypeExportService.ExportBlankTemplate(TemplateFilePath);
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
