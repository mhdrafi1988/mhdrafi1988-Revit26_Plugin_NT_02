using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Models;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Core.Services
{
    /// <summary>
    /// Scans the document for the CAD-link and CAD-import instances inside a
    /// <see cref="DwgScanScope"/> and produces a <see cref="DwgInstanceInfo"/> descriptor for each one.
    /// </summary>
    public class DwgCollectorService
    {
        private readonly Document _doc;

        // Caps on how many views to scan per category when resolving
        // model-space imports (no OwnerViewId).  Keeps scan time bounded
        // on projects with hundreds of views.
        private const int MaxViewsToScan = 150;

        /// <summary>Progress stage reported while model-space imports are matched to views.</summary>
        public const string StageFindingViews = "Finding views";

        /// <summary>
        /// Instances left out of the last <see cref="Collect"/> because another in-scope instance of
        /// the same source file came first (NEW in 4.2.0).
        /// </summary>
        public int DuplicatesSkipped { get; private set; }

        /// <summary>Progress stage reported per DWG instance.</summary>
        public const string StageScanning = "Scanning";

        public DwgCollectorService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Returns one <see cref="DwgInstanceInfo"/> per ImportInstance that falls inside
        /// <paramref name="scope"/> (source type and host-view category).
        /// </summary>
        /// <param name="scope">What to collect. Nothing is collected when it has no source or no view type.</param>
        /// <param name="progress">
        /// Optional progress callback: (stage, current, total). Stage is "Finding views" while
        /// model-space imports are matched to views, then "Scanning" per DWG instance.
        /// </param>
        /// <param name="nameTemplate">
        /// NEW in V004. Output-name pattern, e.g. <c>{sheet}-{name}</c>. Null or empty keeps V003's
        /// behaviour (the CAD type name). See <see cref="ApplyNameTemplate"/> for the tokens.
        /// </param>
        public List<DwgInstanceInfo> Collect(DwgScanScope scope, Action<string, int, int> progress = null,
                                             string nameTemplate = null)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            var result = new List<DwgInstanceInfo>();
            DuplicatesSkipped = 0;
            var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!scope.IsValid)
            {
                progress?.Invoke(StageScanning, 0, 0);
                return result;
            }

            // Source filter first — cheap, and keeps the view pass below small.
            var instances = new FilteredElementCollector(_doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(i => scope.Includes(i.IsLinked ? DwgSourceType.Link : DwgSourceType.Import))
                .ToList();

            if (instances.Count == 0)
            {
                progress?.Invoke(StageScanning, 0, 0);
                return result;
            }

            // Build sheet-placement map: viewId → "A101 - Floor Plan Level 1"
            var sheetMap = BuildSheetMap();

            // Model-space imports (OwnerViewId invalid) are classified by the first plan / section
            // view they show in. That pass is needed whenever Plan, Section or No Views is in scope
            // (No Views = "not found in any plan or section"); model-space imports are never Drafting.
            var modelSpaceIds = instances
                .Where(i => i.OwnerViewId == ElementId.InvalidElementId)
                .Select(i => i.Id)
                .ToHashSet();

            bool needViewPass = modelSpaceIds.Count > 0 && (scope.Plan || scope.Section || scope.NoViews);
            var modelSpaceCategoryMap = needViewPass
                ? BuildModelSpaceCategoryMap(modelSpaceIds, progress)
                : new Dictionary<ElementId, (DwgViewCategory, ElementId, string)>();

            var outputNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int n = 0; n < instances.Count; n++)
            {
                ToolCancellation.CheckCurrent();
                var inst = instances[n];
                progress?.Invoke(StageScanning, n + 1, instances.Count);

                var info = new DwgInstanceInfo();
                info.ElementId = inst.Id;
                info.SourceType = inst.IsLinked ? DwgSourceType.Link : DwgSourceType.Import;
                info.SymbolName = GetSymbolName(inst);

                // Determine view category and host view
                if (inst.OwnerViewId != ElementId.InvalidElementId)
                {
                    var view = _doc.GetElement(inst.OwnerViewId) as View;
                    info.OwnerViewId = inst.OwnerViewId;
                    info.ViewName = view?.Name ?? string.Empty;
                    info.ViewCategory = ClassifyView(view);
                }
                else if (modelSpaceCategoryMap.TryGetValue(inst.Id, out var tuple))
                {
                    info.OwnerViewId = tuple.Item2;
                    info.ViewCategory = tuple.Item1;
                    var v = _doc.GetElement(tuple.Item2) as View;
                    info.ViewName = v?.Name ?? string.Empty;
                }
                else
                {
                    info.OwnerViewId = ElementId.InvalidElementId;
                    info.ViewCategory = DwgViewCategory.NoViews;
                    info.ViewName = string.Empty;
                }

                // View-type filter
                if (!scope.Includes(info.ViewCategory))
                    continue;

                if (inst.IsLinked)
                    info.LinkedSourcePath = TryGetLinkedSourcePath(inst);

                // 4.2.0: one export per source file. The first in-scope instance found wins; later
                // instances of the same file (placed in other views) are left out of the list.
                if (!seenSources.Add(SourceKey(inst, info)))
                {
                    DuplicatesSkipped++;
                    continue;
                }

                // Sheet info
                if (info.OwnerViewId != ElementId.InvalidElementId &&
                    sheetMap.TryGetValue(info.OwnerViewId, out string sheetLabel))
                    info.SheetInfo = sheetLabel;
                else
                    info.SheetInfo = "Not on Sheet";

                // V004: how many layers this instance brings in, for the grid column and the
                // hover tooltip. Read before the name is built so {layers} could be used one day.
                info.LayerCount = CountLayers(inst);

                // Assign a unique output file name
                string baseName = SanitizeFileName(ApplyNameTemplate(nameTemplate, info));
                if (string.IsNullOrWhiteSpace(baseName)) baseName = $"CAD_{inst.Id.Value}";

                if (!baseName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                    baseName += ".dwg";

                info.OutputFileName = UniqueOutputName(baseName, outputNameCounts);
                result.Add(info);
            }

            return result;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// NEW in V004. The tokens an output-name template may use, in the order they are listed in
        /// the window's hint text.
        /// </summary>
        public static readonly string[] NameTokens =
            { "{name}", "{sheet}", "{sheetnumber}", "{view}", "{viewtype}", "{source}", "{id}" };

        /// <summary>Default template — V003's behaviour, the CAD type name on its own.</summary>
        public const string DefaultNameTemplate = "{name}";

        /// <summary>
        /// NEW in V004. Fills <paramref name="template"/> from <paramref name="info"/>. An empty or
        /// null template, or one that resolves to nothing (every token empty), falls back to the CAD
        /// type name, so a careless pattern can never produce a file called ".dwg".
        /// <para>
        /// Tokens: <c>{name}</c> the CAD type name · <c>{sheet}</c> the full sheet label ·
        /// <c>{sheetnumber}</c> just the number before the first " - " · <c>{view}</c> host view
        /// name · <c>{viewtype}</c> Plan/Section/Drafting/NoViews · <c>{source}</c> Link or Import ·
        /// <c>{id}</c> the element id.
        /// </para>
        /// </summary>
        public static string ApplyNameTemplate(string template, DwgInstanceInfo info)
        {
            string fallback = info?.SymbolName ?? string.Empty;
            if (info == null || string.IsNullOrWhiteSpace(template)) return fallback;

            // The type name usually already ends in .dwg; strip it so "{name}-{sheet}" doesn't
            // produce "GRID.dwg-A101".
            string bareName = fallback.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
                ? fallback.Substring(0, fallback.Length - 4)
                : fallback;

            string sheet = info.SheetInfo ?? string.Empty;
            if (string.Equals(sheet, "Not on Sheet", StringComparison.OrdinalIgnoreCase))
                sheet = string.Empty;

            string sheetNumber = sheet;
            int dash = sheet.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) sheetNumber = sheet.Substring(0, dash);

            string viewType = info.ViewCategory switch
            {
                DwgViewCategory.Plan => "Plan",
                DwgViewCategory.Section => "Section",
                DwgViewCategory.Drafting => "Drafting",
                _ => "NoViews"
            };

            string result = template
                .Replace("{name}", bareName)
                .Replace("{sheetnumber}", sheetNumber)
                .Replace("{sheet}", sheet)
                .Replace("{viewtype}", viewType)
                .Replace("{view}", info.ViewName ?? string.Empty)
                .Replace("{source}", info.SourceType == DwgSourceType.Link ? "Link" : "Import")
                .Replace("{id}", info.ElementId?.Value.ToString() ?? string.Empty);

            // Tidy up what empty tokens leave behind: doubled, leading or trailing separators.
            result = result.Trim();
            while (result.Contains("--")) result = result.Replace("--", "-");
            while (result.Contains("__")) result = result.Replace("__", "_");
            result = result.Trim('-', '_', ' ', '.');

            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }

        /// <summary>
        /// NEW in V004. How many layers this CAD instance brings in. Revit models an imported or
        /// linked DWG's layers as subcategories of a category named after the file, so the count is
        /// that category's subcategory count. Returns -1 when it can't be read — a count is a
        /// diagnostic, never a reason to fail a scan.
        /// </summary>
        private int CountLayers(ImportInstance inst)
        {
            try
            {
                var category = inst.Category;
                if (category?.SubCategories != null && category.SubCategories.Size > 0)
                    return category.SubCategories.Size;

                // Fall back to the import-object-styles tree, where each child category is one
                // imported file and its own children are that file's layers.
                var importStyles = _doc.Settings?.Categories?.get_Item(BuiltInCategory.OST_ImportObjectStyles);
                if (importStyles?.SubCategories == null) return -1;

                string wanted = GetSymbolName(inst);
                foreach (Category fileCategory in importStyles.SubCategories)
                {
                    ToolCancellation.CheckCurrent();
                    if (!string.Equals(fileCategory?.Name, wanted, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return fileCategory.SubCategories?.Size ?? 0;
                }

                return -1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Identity of the source file behind an instance: the linked file path for a link (so two
        /// link types pointing at one file match), otherwise the CAD type id.
        /// </summary>
        private static string SourceKey(ImportInstance inst, DwgInstanceInfo info)
        {
            if (info.SourceType == DwgSourceType.Link && !string.IsNullOrWhiteSpace(info.LinkedSourcePath))
                return "L|" + info.LinkedSourcePath.Trim();
            return (info.SourceType == DwgSourceType.Link ? "LT|" : "I|") + inst.GetTypeId().Value;
        }

        private string GetSymbolName(ImportInstance inst)
        {
            try
            {
                var type = _doc.GetElement(inst.GetTypeId());
                return type?.Name ?? inst.Name ?? string.Empty;
            }
            catch
            {
                return inst.Name ?? string.Empty;
            }
        }

        private string TryGetLinkedSourcePath(ImportInstance inst)
        {
            try
            {
                var type = _doc.GetElement(inst.GetTypeId()) as CADLinkType;
                if (type == null) return null;
                var extRef = type.GetExternalFileReference();
                if (extRef == null) return null;
                return ModelPathUtils.ConvertModelPathToUserVisiblePath(extRef.GetAbsolutePath());
            }
            catch
            {
                return null;
            }
        }

        private static DwgViewCategory ClassifyView(View view)
        {
            if (view == null) return DwgViewCategory.NoViews;
            return view switch
            {
                ViewPlan => DwgViewCategory.Plan,
                ViewSection => DwgViewCategory.Section,
                ViewDrafting => DwgViewCategory.Drafting,
                _ => DwgViewCategory.NoViews
            };
        }

        /// <summary>
        /// Scans plan and section views (in priority order) to map each model-space
        /// ImportInstance to its first-found view category. Reports progress per view.
        /// </summary>
        private Dictionary<ElementId, (DwgViewCategory, ElementId, string)> BuildModelSpaceCategoryMap(
            HashSet<ElementId> targetIds, Action<string, int, int> progress)
        {
            var map = new Dictionary<ElementId, (DwgViewCategory, ElementId, string)>();

            var planViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                .Take(MaxViewsToScan)
                .Cast<View>()
                .ToList();

            var sectionViews = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSection))
                .Cast<ViewSection>()
                .Where(v => !v.IsTemplate)
                .Take(MaxViewsToScan)
                .Cast<View>()
                .ToList();

            int total = planViews.Count + sectionViews.Count;
            int done = 0;
            progress?.Invoke(StageFindingViews, 0, total);

            ScanViewsForCategory(planViews, DwgViewCategory.Plan, targetIds, map, total, ref done, progress);
            ScanViewsForCategory(sectionViews, DwgViewCategory.Section, targetIds, map, total, ref done, progress);

            progress?.Invoke(StageFindingViews, total, total);
            return map;
        }

        private void ScanViewsForCategory(
            IEnumerable<View> views,
            DwgViewCategory category,
            HashSet<ElementId> targetIds,
            Dictionary<ElementId, (DwgViewCategory, ElementId, string)> map,
            int total,
            ref int done,
            Action<string, int, int> progress)
        {
            foreach (var view in views)
            {
                ToolCancellation.CheckCurrent();
                if (map.Count == targetIds.Count) break; // all resolved

                done++;
                progress?.Invoke(StageFindingViews, done, total);

                try
                {
                    foreach (var elem in new FilteredElementCollector(_doc, view.Id)
                                 .OfClass(typeof(ImportInstance)))
                    {
                        if (targetIds.Contains(elem.Id) && !map.ContainsKey(elem.Id))
                            map[elem.Id] = (category, view.Id, view.Name);
                    }
                }
                catch
                {
                    // A view that cannot be collected (e.g., template edge case) — skip it.
                }
            }
        }

        /// <summary>
        /// Builds a map from view ElementId to its sheet label ("A101 - Sheet Name"),
        /// covering all views placed on sheets via Viewport elements.
        /// </summary>
        private Dictionary<ElementId, string> BuildSheetMap()
        {
            var map = new Dictionary<ElementId, string>();
            try
            {
                var viewports = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>();

                foreach (var vp in viewports)
                {
                    ToolCancellation.CheckCurrent();
                    try
                    {
                        if (map.ContainsKey(vp.ViewId)) continue;

                        var sheet = _doc.GetElement(vp.SheetId) as ViewSheet;
                        if (sheet == null) continue;

                        string label = $"{sheet.SheetNumber} - {sheet.Name}";
                        map[vp.ViewId] = label;
                    }
                    catch { }
                }
            }
            catch { }
            return map;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static string UniqueOutputName(string baseName, Dictionary<string, int> counts)
        {
            string key = baseName.ToUpperInvariant();
            if (!counts.TryGetValue(key, out int n))
            {
                counts[key] = 1;
                return baseName;
            }
            counts[key] = n + 1;
            string stem = Path.GetFileNameWithoutExtension(baseName);
            string ext = Path.GetExtension(baseName);
            return $"{stem} ({n + 1}){ext}";
        }
    }
}
