using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DwgStatistics.V005.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgStatistics.V005.Infrastructure.RevitApi
{
    /// <summary>
    /// V004: state of one deep scan that runs in batches. Between batches Revit returns to its message
    /// loop, so the window repaints, the progress bar moves and Cancel can be clicked.
    /// Only touched from the Revit API context (the external event).
    /// </summary>
    public sealed class DwgScanSession
    {
        internal DwgScanSession(DwgScanResult result, List<ElementId> views,
            Dictionary<ElementId, SheetPlacement> sheetsByView, List<ElementId> modelInstanceIds,
            Dictionary<long, List<ElementId>> ownedByView)
        {
            Result = result;
            Views = views;
            SheetsByView = sheetsByView;
            ModelInstanceIds = modelInstanceIds;
            OwnedByView = ownedByView;
            Watch = Stopwatch.StartNew();
        }

        /// <summary>Facts collected so far.</summary>
        public DwgScanResult Result { get; }

        internal List<ElementId> Views { get; }
        internal Dictionary<ElementId, SheetPlacement> SheetsByView { get; }
        internal List<ElementId> ModelInstanceIds { get; }
        internal Dictionary<long, List<ElementId>> OwnedByView { get; }
        internal Stopwatch Watch { get; }

        /// <summary>V005: instance ID → its CAD import category ID (for the per-view layer check).</summary>
        internal Dictionary<long, long> CategoryByInstance { get; } = new Dictionary<long, long>();
        internal int Next { get; set; }

        /// <summary>Views to check in total.</summary>
        public int TotalViews => Views.Count;

        /// <summary>Views checked so far.</summary>
        public int DoneViews => Next;

        /// <summary>True when every view has been checked.</summary>
        public bool IsComplete => Next >= Views.Count;

        /// <summary>0–100.</summary>
        public double Percent => Views.Count == 0 ? 100 : 100.0 * Next / Views.Count;
    }

    /// <summary>Sheets a view is placed on.</summary>
    internal sealed class SheetPlacement
    {
        public List<string> Numbers { get; } = new List<string>();
        public List<long> Ids { get; } = new List<long>();
    }

    /// <summary>
    /// Reads every CAD type, every CAD instance and — through a deep scan of every graphical view —
    /// which views draw each instance and (V004) where model instances are hidden by element.
    /// Must run in a Revit API context. Read-only: opens no transaction.
    /// V004: <see cref="Begin"/> reads types/instances/sheets, then <see cref="ScanBatch"/> is called
    /// once per external-event run until the session is complete or cancelled.
    /// </summary>
    public static class DwgStatsScanner
    {
        /// <summary>View types the deep scan checks. Schedules, browsers and the like cannot hold CAD.</summary>
        private static readonly HashSet<ViewType> GraphicalViewTypes = new HashSet<ViewType>
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan,
            ViewType.Section, ViewType.Elevation, ViewType.Detail, ViewType.ThreeD,
            ViewType.DraftingView, ViewType.Legend, ViewType.DrawingSheet
        };

        /// <summary>View types where a model-placed CAD instance can appear (and so can be hidden).</summary>
        private static readonly HashSet<ViewType> ModelViewTypes = new HashSet<ViewType>
        {
            ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.EngineeringPlan, ViewType.AreaPlan,
            ViewType.Section, ViewType.Elevation, ViewType.Detail, ViewType.ThreeD
        };

        /// <summary>
        /// Reads types, instances and sheet placements, and lists the views to deep-scan. Fast; the slow
        /// per-view part is done by <see cref="ScanBatch"/>.
        /// </summary>
        public static DwgScanSession Begin(Document doc, Action<LogEntry> log)
        {
            var result = new DwgScanResult
            {
                ProjectTitle = doc?.Title ?? string.Empty,
                ProjectKey = ProjectKeyOf(doc),
                ProjectFolder = doc == null ? string.Empty : ProjectFolderOf(doc)
            };

            var instances = doc == null
                ? new List<ImportInstance>()
                : new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();

            var sheetsByView = doc == null ? new Dictionary<ElementId, SheetPlacement>() : ReadSheetPlacements(doc);
            var views = new List<ElementId>();
            var modelIds = new List<ElementId>();
            var owned = new Dictionary<long, List<ElementId>>();

            if (doc != null)
            {
                ReadWorksets(doc, result);
                ReadTypes(doc, instances, result, log);
                ReadInstances(doc, instances, result, log);

                foreach (var inst in instances)
                {
                    ToolCancellation.CheckCurrent();
                    var owner = inst.OwnerViewId ?? ElementId.InvalidElementId;
                    if (inst.ViewSpecific && owner != ElementId.InvalidElementId)
                    {
                        if (!owned.TryGetValue(owner.Value, out var list))
                        {
                            list = new List<ElementId>();
                            owned[owner.Value] = list;
                        }
                        list.Add(inst.Id);
                    }
                    else
                    {
                        modelIds.Add(inst.Id);
                    }
                }

                // Owner views always get a ViewFact, even if the deep scan cannot read them.
                foreach (var inst in result.Instances.Where(i => i.IsViewSpecific && i.OwnerViewId != 0))
                    EnsureViewFact(doc, inst.OwnerViewId, result, sheetsByView);

                // Nothing to look for → no views to scan.
                if (instances.Count > 0)
                {
                    views = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && GraphicalViewTypes.Contains(v.ViewType))
                        .Select(v => v.Id)
                        .ToList();
                }
            }

            var session = new DwgScanSession(result, views, sheetsByView, modelIds, owned);
            foreach (var fact in result.Instances.Where(f => f.CategoryId != 0))
                session.CategoryByInstance[fact.Id] = fact.CategoryId;
            return session;
        }

        /// <summary>
        /// Checks views until <paramref name="maxMilliseconds"/> have passed (at least one view per call).
        /// Returns true when the session is complete; the elapsed time and a log line are set then.
        /// </summary>
        public static bool ScanBatch(Document doc, DwgScanSession session, int maxMilliseconds, Action<LogEntry> log)
        {
            var batch = Stopwatch.StartNew();
            var result = session.Result;

            while (!session.IsComplete)
            {
                ToolCancellation.CheckCurrent();
                var viewId = session.Views[session.Next++];
                ScanOneView(doc, viewId, session);
                if (batch.ElapsedMilliseconds >= maxMilliseconds) break;
            }

            if (!session.IsComplete) return false;

            session.Watch.Stop();
            result.Elapsed = session.Watch.Elapsed;
            int hidden = result.HiddenIn.Values.Sum(s => s.Count);
            log?.Invoke(new LogEntry(LogLevel.Info,
                $"Deep scan: {result.ViewsScanned} view(s) checked" +
                (result.ViewsSkipped > 0 ? $", {result.ViewsSkipped} skipped (cannot be read)" : string.Empty) +
                (hidden > 0 ? $", {hidden} hidden-in-view placement(s) found" : string.Empty) +
                $" in {result.Elapsed.TotalSeconds:0.0} s."));
            return true;
        }

        private static void ScanOneView(Document doc, ElementId viewId, DwgScanSession session)
        {
            var result = session.Result;
            if (doc.GetElement(viewId) is not View view || !view.IsValidObject)
            {
                result.ViewsSkipped++;
                return;
            }

            HashSet<ElementId> drawn;
            try
            {
                // Disposed right away: one collector per view, hundreds per scan (native Revit memory).
                using (var collector = new FilteredElementCollector(doc, view.Id))
                    drawn = new HashSet<ElementId>(collector.OfClass(typeof(ImportInstance)).ToElementIds());
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                result.ViewsSkipped++;
                return;
            }

            result.ViewsScanned++;
            long vid = view.Id.Value;
            bool touched = false;

            foreach (var id in drawn)
            {
                ToolCancellation.CheckCurrent();
                AddTo(result.VisibleIn, id.Value, vid);
                touched = true;
            }

            // V004: where are instances hidden by element? Model instances in model views, plus the
            // view's own "Current view only" instances.
            var candidates = new List<ElementId>();
            if (ModelViewTypes.Contains(view.ViewType)) candidates.AddRange(session.ModelInstanceIds);
            if (session.OwnedByView.TryGetValue(vid, out var own)) candidates.AddRange(own);

            foreach (var id in candidates)
            {
                ToolCancellation.CheckCurrent();
                if (drawn.Contains(id)) continue;
                var element = doc.GetElement(id);
                if (element == null) continue;
                try
                {
                    if (element.IsHidden(view))
                    {
                        AddTo(result.HiddenIn, id.Value, vid);
                        touched = true;
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // Element/view pair Revit cannot evaluate: treat as not hidden.
                }
            }

            // V005: layer visibility of each CAD file drawn or placed in this view.
            var categories = new HashSet<long>();
            foreach (var id in drawn)
                if (session.CategoryByInstance.TryGetValue(id.Value, out long cat)) categories.Add(cat);
            if (session.OwnedByView.TryGetValue(vid, out var ownedHere))
                foreach (var id in ownedHere)
                    if (session.CategoryByInstance.TryGetValue(id.Value, out long cat)) categories.Add(cat);

            foreach (long cat in categories)
            {
                ToolCancellation.CheckCurrent();
                if (!result.LayersByCategory.TryGetValue(cat, out var layers) || layers.Count == 0) continue;
                var state = new LayerViewState();
                foreach (var layer in layers)
                {
                    ToolCancellation.CheckCurrent();
                    var layerId = new ElementId(layer.LayerId);
                    try
                    {
                        if (view.GetCategoryHidden(layerId)) state.Hidden.Add(layer.LayerId);
                        if (IsOverridden(view.GetCategoryOverrides(layerId))) state.Overridden.Add(layer.LayerId);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // Layer not applicable to this view kind: skip it.
                    }
                }

                if (state.Hidden.Count == 0 && state.Overridden.Count == 0) continue;
                if (!result.LayerStates.TryGetValue(vid, out var byCat))
                {
                    byCat = new Dictionary<long, LayerViewState>();
                    result.LayerStates[vid] = byCat;
                }
                byCat[cat] = state;
                touched = true;
            }

            if (touched) EnsureViewFact(view, result, session.SheetsByView);
        }

        private static void AddTo(Dictionary<long, HashSet<long>> map, long key, long value)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<long>();
                map[key] = set;
            }
            set.Add(value);
        }

        // Central model path for workshared models (same for every local copy), else the file path,
        // else the title — the key of the last-scan snapshot.
        private static string ProjectKeyOf(Document doc)
        {
            if (doc == null) return string.Empty;
            try
            {
                if (doc.IsWorkshared)
                {
                    var central = doc.GetWorksharingCentralModelPath();
                    if (central != null)
                    {
                        string p = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                        if (!string.IsNullOrWhiteSpace(p)) return p;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Fall back to the file path / title.
            }
            return string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
        }

        // ── Types ────────────────────────────────────────────────────────────

        private static void ReadTypes(Document doc, List<ImportInstance> instances, DwgScanResult result, Action<LogEntry> log)
        {
            // Every CAD type (also those with no instance), plus any other type an instance uses.
            var typeIds = new HashSet<ElementId>(new FilteredElementCollector(doc)
                .OfClass(typeof(CADLinkType))
                .ToElementIds());
            foreach (var inst in instances)
            {
                ToolCancellation.CheckCurrent();
                var id = inst.GetTypeId();
                if (id != null && id != ElementId.InvalidElementId) typeIds.Add(id);
            }

            int unreadable = 0;
            foreach (var typeId in typeIds)
            {
                ToolCancellation.CheckCurrent();
                try
                {
                    var fact = ReadType(doc, typeId);
                    if (fact != null) result.Types.Add(fact);
                    else unreadable++;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    unreadable++;
                }
            }

            if (unreadable > 0)
                log?.Invoke(new LogEntry(LogLevel.Warning, $"{unreadable} CAD type(s) could not be read and were skipped."));
        }

        private static CadTypeFact ReadType(Document doc, ElementId typeId)
        {
            if (doc.GetElement(typeId) is not ElementType type) return null;

            string name = type.Name;
            string path = string.Empty;
            string status = "Imported";
            string pathType = string.Empty;
            bool isLinked = false;
            bool isLoaded = true;

            if (ExternalFileUtils.IsExternalFileReference(doc, typeId))
            {
                isLinked = true;
                try
                {
                    var reference = ExternalFileUtils.GetExternalFileReference(doc, typeId);
                    if (reference != null)
                    {
                        var linkStatus = reference.GetLinkedFileStatus();
                        isLoaded = linkStatus == LinkedFileStatus.Loaded;
                        status = Friendly(linkStatus.ToString());
                        pathType = reference.PathType.ToString();

                        var modelPath = reference.GetAbsolutePath();
                        if (modelPath != null)
                            path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath) ?? string.Empty;
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    status = "Unknown";
                    isLoaded = false;
                }
            }

            if (string.IsNullOrWhiteSpace(name) && path.Length > 0) name = global::System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) name = $"(CAD type #{typeId.Value})";

            long? size = null;
            DateTime? modified = null;
            if (path.Length > 0)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        size = info.Length;
                        modified = info.LastWriteTime;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                           ex is ArgumentException || ex is NotSupportedException ||
                                           ex is global::System.Security.SecurityException)
                {
                    // Cloud/server path or no access: size and date stay empty.
                }
            }

            return new CadTypeFact
            {
                TypeId = typeId.Value,
                Name = name,
                IsLinked = isLinked,
                Path = path,
                Status = status,
                IsLoaded = isLoaded,
                PathType = pathType,
                FileSizeBytes = size,
                FileModified = modified
            };
        }

        // "NotFound" → "Not found", "LocallyUnloaded" → "Locally unloaded".
        private static string Friendly(string enumText)
        {
            if (string.IsNullOrEmpty(enumText)) return enumText;
            var sb = new StringBuilder();
            for (int i = 0; i < enumText.Length; i++)
            {
                ToolCancellation.CheckCurrent();
                char c = enumText[i];
                if (i > 0 && char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ── Instances ────────────────────────────────────────────────────────

        private static void ReadInstances(Document doc, List<ImportInstance> instances, DwgScanResult result, Action<LogEntry> log)
        {
            var worksets = doc.IsWorkshared ? doc.GetWorksetTable() : null;
            var levelNames = new Dictionary<ElementId, string>();
            int skipped = 0;

            foreach (var inst in instances)
            {
                ToolCancellation.CheckCurrent();
                try
                {
                    var typeId = inst.GetTypeId();
                    if (typeId == null || typeId == ElementId.InvalidElementId)
                    {
                        skipped++;
                        continue;
                    }

                    var ownerId = inst.OwnerViewId ?? ElementId.InvalidElementId;
                    bool viewSpecific = inst.ViewSpecific && ownerId != ElementId.InvalidElementId;
                    var category = SafeCategory(inst);
                    if (category != null) RecordLayers(category, result);
                    Distances(doc, inst, viewSpecific ? ownerId : null, out double originFt, out double extentFt);

                    result.Instances.Add(new CadInstanceFact
                    {
                        CategoryId = category?.Id.Value ?? 0,
                        OriginDistanceFt = originFt,
                        ExtentDistanceFt = extentFt,
                        WorksetId = doc.IsWorkshared ? inst.WorksetId.IntegerValue : 0,
                        CanChangeWorkset = doc.IsWorkshared && !viewSpecific && CanChangeWorkset(inst),
                        Id = inst.Id.Value,
                        TypeId = typeId.Value,
                        IsViewSpecific = viewSpecific,
                        OwnerViewId = viewSpecific ? ownerId.Value : 0,
                        Pinned = inst.Pinned,
                        Workset = WorksetName(worksets, inst),
                        Level = LevelName(doc, inst.LevelId, levelNames),
                        CreatedBy = Creator(doc, inst.Id),
                        LayerCount = LayerCount(inst)
                    });
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    skipped++;
                }
            }

            if (skipped > 0)
                log?.Invoke(new LogEntry(LogLevel.Warning, $"{skipped} CAD instance(s) skipped: their type could not be read."));
        }

        // ── V005: category / layers, distance, workset helpers ───────────────

        private static Category SafeCategory(ImportInstance inst)
        {
            try { return inst.Category; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        // Each CAD file has its own import category; its sub-categories are the DWG layers.
        private static void RecordLayers(Category category, DwgScanResult result)
        {
            long id = category.Id.Value;
            if (result.LayersByCategory.ContainsKey(id)) return;

            var layers = new List<CadLayerFact>();
            try
            {
                foreach (Category sub in category.SubCategories)
                    layers.Add(new CadLayerFact { LayerId = sub.Id.Value, Name = sub.Name });
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Layers unreadable: the file simply shows no layer rows.
            }
            result.LayersByCategory[id] = layers;
        }

        // Insertion point and farthest bounding-box corner, measured from the internal origin (feet).
        private static void Distances(Document doc, ImportInstance inst, ElementId ownerView, out double originFt, out double extentFt)
        {
            originFt = 0;
            extentFt = 0;
            try
            {
                var origin = inst.GetTotalTransform()?.Origin;
                if (origin != null) originFt = origin.GetLength();

                BoundingBoxXYZ box = inst.get_BoundingBox(null);
                if (box == null && ownerView != null && doc.GetElement(ownerView) is View owner)
                    box = inst.get_BoundingBox(owner);
                if (box != null)
                {
                    // Farthest corner of an axis-aligned box: the larger |value| on each axis.
                    double x = Math.Max(Math.Abs(box.Min.X), Math.Abs(box.Max.X));
                    double y = Math.Max(Math.Abs(box.Min.Y), Math.Abs(box.Max.Y));
                    double z = Math.Max(Math.Abs(box.Min.Z), Math.Abs(box.Max.Z));
                    extentFt = Math.Sqrt(x * x + y * y + z * z);
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Geometry unavailable (e.g. unloaded link): distance stays 0.
            }
        }

        private static bool CanChangeWorkset(Element element)
        {
            try
            {
                var p = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                return p != null && !p.IsReadOnly;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return false;
            }
        }

        private static void ReadWorksets(Document doc, DwgScanResult result)
        {
            result.IsWorkshared = doc.IsWorkshared;
            if (!doc.IsWorkshared) return;
            try
            {
                foreach (var ws in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets())
                    result.Worksets.Add(new WorksetFact { Id = ws.Id.IntegerValue, Name = ws.Name });
                result.Worksets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // No workset list: the workset rule still runs on instance workset names.
            }
        }

        // Folder that holds the project file (central model folder for workshared files). Empty for
        // cloud / Revit Server / unsaved models, where a shared history file cannot be placed.
        private static string ProjectFolderOf(Document doc)
        {
            try
            {
                string path = string.Empty;
                if (doc.IsWorkshared)
                {
                    var central = doc.GetWorksharingCentralModelPath();
                    if (central != null) path = ModelPathUtils.ConvertModelPathToUserVisiblePath(central) ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(path)) path = doc.PathName ?? string.Empty;

                if (string.IsNullOrWhiteSpace(path) || path.Contains("://") ||
                    path.StartsWith("RSN:", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("BIM 360", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("Autodesk Docs", StringComparison.OrdinalIgnoreCase) ||
                    !global::System.IO.Path.IsPathRooted(path))
                    return string.Empty;

                return global::System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is ArgumentException ||
                                       ex is global::System.IO.PathTooLongException)
            {
                return string.Empty;
            }
        }

        // Any visibility/graphics override on a layer in a view.
        private static bool IsOverridden(OverrideGraphicSettings o)
        {
            if (o == null) return false;
            return o.Halftone || o.Transparency > 0
                || o.ProjectionLineColor.IsValid || o.ProjectionLineWeight != OverrideGraphicSettings.InvalidPenNumber
                || o.ProjectionLinePatternId != ElementId.InvalidElementId
                || o.CutLineColor.IsValid || o.CutLineWeight != OverrideGraphicSettings.InvalidPenNumber
                || o.CutLinePatternId != ElementId.InvalidElementId
                || o.SurfaceForegroundPatternId != ElementId.InvalidElementId
                || o.SurfaceBackgroundPatternId != ElementId.InvalidElementId
                || o.DetailLevel != ViewDetailLevel.Undefined;
        }

        private static string WorksetName(WorksetTable table, Element element)
        {
            if (table == null) return string.Empty;
            try
            {
                return table.GetWorkset(element.WorksetId)?.Name ?? string.Empty;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return string.Empty;
            }
        }

        private static string LevelName(Document doc, ElementId levelId, Dictionary<ElementId, string> cache)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId) return string.Empty;
            if (cache.TryGetValue(levelId, out var name)) return name;
            name = (doc.GetElement(levelId) as Level)?.Name ?? string.Empty;
            cache[levelId] = name;
            return name;
        }

        private static string Creator(Document doc, ElementId id)
        {
            if (!doc.IsWorkshared) return string.Empty;
            try
            {
                return WorksharingUtils.GetWorksharingTooltipInfo(doc, id)?.Creator ?? string.Empty;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return string.Empty;
            }
        }

        // Each CAD file gets its own import category; its sub-categories are the DWG layers.
        private static int LayerCount(ImportInstance inst)
        {
            try
            {
                return inst.Category?.SubCategories?.Size ?? 0;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 0;
            }
        }

        // ── Views and sheets ─────────────────────────────────────────────────

        // View → sheet numbers and IDs, from every viewport in the project.
        private static Dictionary<ElementId, SheetPlacement> ReadSheetPlacements(Document doc)
        {
            var map = new Dictionary<ElementId, SheetPlacement>();
            var sheetNumbers = new Dictionary<ElementId, string>();

            foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                ToolCancellation.CheckCurrent();
                if (!sheetNumbers.TryGetValue(vp.SheetId, out var number))
                {
                    number = (doc.GetElement(vp.SheetId) as ViewSheet)?.SheetNumber ?? string.Empty;
                    sheetNumbers[vp.SheetId] = number;
                }
                if (number.Length == 0) continue;

                if (!map.TryGetValue(vp.ViewId, out var placement))
                {
                    placement = new SheetPlacement();
                    map[vp.ViewId] = placement;
                }
                if (!placement.Numbers.Contains(number))
                {
                    placement.Numbers.Add(number);
                    placement.Ids.Add(vp.SheetId.Value);
                }
            }

            return map;
        }

        private static void EnsureViewFact(Document doc, long viewId, DwgScanResult result,
            Dictionary<ElementId, SheetPlacement> sheetsByView)
        {
            if (result.Views.ContainsKey(viewId)) return;

            if (doc.GetElement(new ElementId(viewId)) is View view)
            {
                EnsureViewFact(view, result, sheetsByView);
            }
            else
            {
                result.Views[viewId] = new ViewFact { Id = viewId, Name = $"(view #{viewId})", TypeLabel = "Other Views" };
            }
        }

        private static void EnsureViewFact(View view, DwgScanResult result, Dictionary<ElementId, SheetPlacement> sheetsByView)
        {
            long id = view.Id.Value;
            if (result.Views.ContainsKey(id)) return;

            var numbers = new List<string>();
            var ids = new List<long>();
            if (view is ViewSheet sheet)
            {
                numbers.Add(sheet.SheetNumber);
                ids.Add(sheet.Id.Value);
            }
            else if (sheetsByView.TryGetValue(view.Id, out var onSheets))
            {
                numbers.AddRange(onSheets.Numbers);
                ids.AddRange(onSheets.Ids);
            }

            string name = view is ViewSheet vs ? $"{vs.SheetNumber} - {vs.Name}" : view.Name;

            result.Views[id] = new ViewFact
            {
                Id = id,
                Name = name,
                TypeLabel = CategoryLabel(view.ViewType),
                SheetNumbers = numbers,
                SheetIds = ids
            };
        }


        /// <summary>View type label — the same groups DWG Manager V003 uses.</summary>
        public static string CategoryLabel(ViewType viewType)
        {
            switch (viewType)
            {
                case ViewType.FloorPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                    return "Floor Plans";
                case ViewType.CeilingPlan:
                    return "Ceiling Plans";
                case ViewType.Elevation:
                    return "Elevations";
                case ViewType.Section:
                    return "Sections";
                case ViewType.Detail:
                    return "Detail Views";
                case ViewType.ThreeD:
                    return "3D Views";
                case ViewType.DraftingView:
                    return "Drafting Views";
                case ViewType.Legend:
                    return "Legends";
                case ViewType.DrawingSheet:
                    return "Sheets";
                default:
                    return "Other Views";
            }
        }
    }
}

