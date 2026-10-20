using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Infrastructure.Revit
{
    /// <summary>
    /// Reads the model into the plain Core models. Read-only; must run in a Revit API context.
    /// Only elements of the host model are read: linked models and view-specific elements are skipped.
    /// </summary>
    public static class ModelReader
    {
        // Model categories that cannot carry a per-element tracking ID in a useful way.
        private static readonly HashSet<BuiltInCategory> Excluded = new()
        {
            BuiltInCategory.OST_RvtLinks, BuiltInCategory.OST_IOSModelGroups, BuiltInCategory.OST_Cameras,
            BuiltInCategory.OST_VolumeOfInterest, BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids,
            BuiltInCategory.OST_Rooms, BuiltInCategory.OST_Areas, BuiltInCategory.OST_MEPSpaces,
            BuiltInCategory.OST_HVAC_Zones, BuiltInCategory.OST_ProjectBasePoint, BuiltInCategory.OST_SharedBasePoint,
            BuiltInCategory.OST_Lines, BuiltInCategory.OST_SketchLines, BuiltInCategory.OST_PointClouds,
            BuiltInCategory.OST_DuctSystem, BuiltInCategory.OST_PipingSystem, BuiltInCategory.OST_ElectricalCircuit
        };

        // Where an element keeps its level when Element.LevelId is not set, tried in order.
        private static readonly BuiltInParameter[] LevelParameters =
        {
            BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.WALL_BASE_CONSTRAINT,
            BuiltInParameter.RBS_START_LEVEL_PARAM, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM, BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
            BuiltInParameter.LEVEL_PARAM
        };

        // ── Step 1: categories ────────────────────────────────────────────────

        /// <summary>
        /// Every model category that can take the Tracker parameters, with its number of placed types,
        /// elements and elements that already have a Tracker_ID. Categories with nothing placed are
        /// included with zero counts.
        /// </summary>
        public static List<CategoryInfo> ReadCategories(Document doc, ReadScope scope = null)
        {
            var stats = new Dictionary<long, (Category Cat, HashSet<long> Types, int Count, int WithId)>();

            foreach (Element e in PlacedElements(doc, scope))
            {
                var cat = e.Category;
                if (!IsTrackable(cat)) continue;

                long typeId = e.GetTypeId().Value;
                if (!stats.TryGetValue(cat.Id.Value, out var s)) s = (cat, new HashSet<long>(), 0, 0);
                s.Types.Add(typeId);
                s.Count++;
                if (HasTrackerId(e)) s.WithId++;
                stats[cat.Id.Value] = s;
            }

            foreach (Category cat in doc.Settings.Categories)
            {
                if (IsTrackable(cat) && !stats.ContainsKey(cat.Id.Value))
                    stats[cat.Id.Value] = (cat, new HashSet<long>(), 0, 0);
            }

            return stats.Values
                .Select(s => new CategoryInfo(s.Cat.Id.Value, s.Cat.Name, NameRules.Discipline(s.Cat.BuiltInCategory.ToString()),
                                              s.Types.Count, s.Count, s.WithId))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        // ── Step 2: types ─────────────────────────────────────────────────────

        /// <summary>The placed types of the categories <paramref name="categoryIds"/> within <paramref name="scope"/>, with element counts.</summary>
        public static List<TypeInfo> ReadTypes(Document doc, ISet<long> categoryIds, ReadScope scope = null)
        {
            var stats = new Dictionary<long, (int Count, int WithId, Category Cat)>();
            foreach (Element e in PlacedElements(doc, scope, categoryIds))
            {
                long typeId = e.GetTypeId().Value;
                stats.TryGetValue(typeId, out var s);
                stats[typeId] = (s.Count + 1, s.WithId + (HasTrackerId(e) ? 1 : 0), e.Category);
            }

            var types = new List<TypeInfo>();
            foreach (var pair in stats)
            {
                var type = doc.GetElement(new ElementId(pair.Key)) as ElementType;
                types.Add(new TypeInfo(pair.Key, type?.FamilyName ?? string.Empty, type?.Name ?? $"Type {pair.Key}",
                                       TypeMark(type), pair.Value.Count, pair.Value.WithId,
                                       pair.Value.Cat?.Id.Value ?? 0, pair.Value.Cat?.Name ?? string.Empty));
            }
            return types.OrderBy(t => t.CategoryName, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(t => t.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(t => t.TypeName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
        }

        // ── Step 3: elements, grids, scope boxes, IDs in use ──────────────────

        /// <summary>
        /// Snapshots every placed element of the chosen categories and types within <paramref name="scope"/>
        /// (all types when <paramref name="typeIds"/> is null). <paramref name="parameterNames"/> are read
        /// from the element (then its type); <paramref name="levelParameterNames"/> from its level.
        /// </summary>
        public static List<ElementSnapshot> ReadSnapshots(Document doc, ISet<long> categoryIds, ISet<long> typeIds,
            IReadOnlyCollection<string> parameterNames, IReadOnlyCollection<string> levelParameterNames, ZoneLocator zones,
            ReadScope scope = null) =>
            ReadElements(doc, PlacedElements(doc, scope, categoryIds), typeIds, parameterNames, levelParameterNames, zones);

        /// <summary>
        /// Ids of the placed elements of <paramref name="categoryIds"/> within <paramref name="scope"/>,
        /// limited to <paramref name="typeIds"/> (null = every type). Fast: reads no parameters. Used to read
        /// a large model in batches (<see cref="ReadSnapshotBatch"/>).
        /// </summary>
        public static List<long> PlacedElementIds(Document doc, ISet<long> categoryIds, ISet<long> typeIds, ReadScope scope = null) =>
            PlacedElements(doc, scope, categoryIds)
                .Where(e => typeIds == null || typeIds.Contains(e.GetTypeId().Value))
                .Select(e => e.Id.Value)
                .ToList();

        /// <summary>
        /// Snapshots of the elements in <paramref name="elementIds"/> (one batch of
        /// <see cref="PlacedElementIds"/>). Elements deleted meanwhile are skipped.
        /// </summary>
        public static List<ElementSnapshot> ReadSnapshotBatch(Document doc, IEnumerable<long> elementIds,
            IReadOnlyCollection<string> parameterNames, IReadOnlyCollection<string> levelParameterNames, ZoneLocator zones) =>
            ReadElements(doc,
                elementIds.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null && e.Category != null),
                null, parameterNames, levelParameterNames, zones);

        private static List<ElementSnapshot> ReadElements(Document doc, IEnumerable<Element> elements, ISet<long> typeIds,
            IReadOnlyCollection<string> parameterNames, IReadOnlyCollection<string> levelParameterNames, ZoneLocator zones)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            var typeCache = new Dictionary<long, ElementType>();
            var snapshots = new List<ElementSnapshot>();

            foreach (Element e in elements)
            {
                long typeId = e.GetTypeId().Value;
                if (typeIds != null && !typeIds.Contains(typeId)) continue;

                if (!typeCache.TryGetValue(typeId, out var type))
                    typeCache[typeId] = type = doc.GetElement(new ElementId(typeId)) as ElementType;

                var s = new ElementSnapshot
                {
                    ElementId = e.Id.Value,
                    UniqueId = e.UniqueId,
                    CategoryId = e.Category.Id.Value,
                    CategoryName = e.Category.Name,
                    TypeId = typeId,
                    FamilyName = type?.FamilyName ?? string.Empty,
                    TypeName = type?.Name ?? string.Empty,
                    TypeMark = TypeMark(type),
                    TrackerId = Text(e, TrackerParameters.TrackerId),
                    SourceGuid = Text(e, TrackerParameters.SourceGuid),
                    StoredZone = Text(e, TrackerParameters.Zone),
                    StoredLevel = Text(e, TrackerParameters.Level),
                    StoredGrid = Text(e, TrackerParameters.Grid),
                    StoredStatus = Text(e, TrackerParameters.Status),
                    StoredProgress = Number(e, TrackerParameters.Progress),
                    StoredStatusDate = Text(e, TrackerParameters.StatusDate),
                    LockReason = WorksharingGuard.OwnerLock(doc, e.Id, out string owner)
                };
                s.LockOwner = owner;

                var idParam = e.LookupParameter(TrackerParameters.TrackerId);
                if (s.LockReason == null && idParam != null && idParam.IsReadOnly)
                    s.LockReason = "Tracker_ID is read-only on this element.";

                XYZ center = Center(e);
                if (center != null)
                {
                    s.HasCenter = true;
                    s.CenterX = center.X;
                    s.CenterY = center.Y;
                    s.ScopeBoxName = zones?.Find(center.X, center.Y) ?? string.Empty;
                }

                var level = ResolveLevel(doc, e, levels, center?.Z);
                if (level != null)
                {
                    s.LevelName = level.Name;
                    s.LevelElevationMm = UnitUtils.ConvertFromInternalUnits(level.ProjectElevation, UnitTypeId.Millimeters);
                    foreach (string name in levelParameterNames)
                    {
                        string v = ParameterText(level.LookupParameter(name));
                        if (v != null) s.LevelParameterValues[name] = v;
                    }
                }

                foreach (string name in parameterNames)
                {
                    string v = ParameterText(e.LookupParameter(name)) ?? ParameterText(type?.LookupParameter(name));
                    if (v != null) s.ParameterValues[name] = v;
                }

                snapshots.Add(s);
            }
            return snapshots;
        }

        /// <summary>
        /// The open file's title and the values of the Project Information parameters named in
        /// <paramref name="projectParameterNames"/> (missing parameters are left out).
        /// </summary>
        public static ModelIdentity ReadModelIdentity(Document doc, IReadOnlyCollection<string> projectParameterNames)
        {
            var identity = new ModelIdentity { Title = doc.Title ?? string.Empty };
            var info = doc.ProjectInformation;
            if (info == null) return identity;

            foreach (string name in projectParameterNames)
            {
                string v = ParameterText(info.LookupParameter(name));
                if (v != null) identity.ProjectParameterValues[name] = v;
            }
            return identity;
        }

        /// <summary>Every grid of the host model, in plan.</summary>
        public static List<GridLine2D> ReadGrids(Document doc)
        {
            var grids = new List<GridLine2D>();
            foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
            {
                switch (g.Curve)
                {
                    case Line line:
                        XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
                        grids.Add(new GridLine2D { Name = g.Name, X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y });
                        break;
                    case Arc arc:
                        grids.Add(new GridLine2D { Name = g.Name, IsCurved = true, CenterX = arc.Center.X, CenterY = arc.Center.Y, Radius = arc.Radius });
                        break;
                }
            }
            return grids;
        }

        /// <summary>Every scope box of the host model, as a plan rectangle (rotation kept).</summary>
        public static List<ZoneBox> ReadScopeBoxes(Document doc)
        {
            var boxes = new List<ZoneBox>();
            var boxElements = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType();

            foreach (Element box in boxElements)
            {
                var points = new List<XYZ>();
                XYZ axis = null;
                if (box.get_Geometry(new Options()) is GeometryElement geometry)
                {
                    foreach (GeometryObject obj in geometry)
                    {
                        if (obj is not Line line) continue;
                        points.Add(line.GetEndPoint(0));
                        points.Add(line.GetEndPoint(1));
                        if (axis == null && Math.Abs(line.Direction.Z) < 1e-3 && line.Length > 1e-6)
                            axis = new XYZ(line.Direction.X, line.Direction.Y, 0).Normalize();
                    }
                }

                if (points.Count == 0)
                {
                    var bb = box.get_BoundingBox(null);
                    if (bb == null) continue;
                    points.Add(bb.Min);
                    points.Add(bb.Max);
                }
                axis ??= XYZ.BasisX;

                var u = points.Select(p => p.X * axis.X + p.Y * axis.Y).ToList();
                var v = points.Select(p => -p.X * axis.Y + p.Y * axis.X).ToList();
                boxes.Add(new ZoneBox
                {
                    Name = box.Name,
                    UX = axis.X,
                    UY = axis.Y,
                    MinU = u.Min(),
                    MaxU = u.Max(),
                    MinV = v.Min(),
                    MaxV = v.Max()
                });
            }
            return boxes;
        }

        /// <summary>Every element in the model that has a Tracker_ID, with its UniqueId and source GUID.</summary>
        public static List<TrackerIdHolder> ReadIdHolders(Document doc)
        {
            var holders = new List<TrackerIdHolder>();
            foreach (Element e in ElementsWithTrackerId(doc))
            {
                string id = Text(e, TrackerParameters.TrackerId);
                if (id.Length > 0)
                    holders.Add(new TrackerIdHolder(e.Id.Value, e.UniqueId, id, Text(e, TrackerParameters.SourceGuid)));
            }
            return holders;
        }

        // ── Step 4: export ────────────────────────────────────────────────────

        /// <summary>
        /// Export rows for every element with a Tracker_ID, or only for <paramref name="elementIds"/> when
        /// given. Zone, level and grid come from the Tracker parameters as last written.
        /// </summary>
        public static List<ExportRow> ReadExportRows(Document doc, IEnumerable<long> elementIds)
        {
            IEnumerable<Element> elements = elementIds == null
                ? ElementsWithTrackerId(doc)
                : elementIds.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null);

            var rows = new List<ExportRow>();
            var typeCache = new Dictionary<long, ElementType>();
            foreach (Element e in elements)
            {
                string id = Text(e, TrackerParameters.TrackerId);
                if (id.Length == 0) continue;

                long typeId = e.GetTypeId().Value;
                if (!typeCache.TryGetValue(typeId, out var type))
                    typeCache[typeId] = type = doc.GetElement(e.GetTypeId()) as ElementType;

                rows.Add(new ExportRow
                {
                    TrackerId = id,
                    Category = e.Category?.Name ?? string.Empty,
                    Family = type?.FamilyName ?? string.Empty,
                    Type = type?.Name ?? string.Empty,
                    TypeMark = TypeMark(type),
                    Zone = Text(e, TrackerParameters.Zone),
                    Level = Text(e, TrackerParameters.Level),
                    Grid = Text(e, TrackerParameters.Grid),
                    LengthM = Quantity(e, UnitTypeId.Meters, BuiltInParameter.CURVE_ELEM_LENGTH, BuiltInParameter.INSTANCE_LENGTH_PARAM),
                    AreaM2 = Quantity(e, UnitTypeId.SquareMeters, BuiltInParameter.HOST_AREA_COMPUTED),
                    VolumeM3 = Quantity(e, UnitTypeId.CubicMeters, BuiltInParameter.HOST_VOLUME_COMPUTED),
                    ElementId = e.Id.Value,
                    UniqueId = e.UniqueId,
                    Status = Text(e, TrackerParameters.Status),
                    ProgressPct = Number(e, TrackerParameters.Progress),
                    StatusDate = Text(e, TrackerParameters.StatusDate)
                });
            }
            return rows.OrderBy(r => r.TrackerId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>True when elements of <paramref name="cat"/> are listed by the tool.</summary>
        public static bool IsTrackable(Category cat) =>
            cat != null && cat.CategoryType == CategoryType.Model && cat.AllowsBoundParameters &&
            !cat.IsTagCategory && cat.Parent == null && !Excluded.Contains(cat.BuiltInCategory);

        /// <summary>
        /// Ids of the trackable categories Tracker_ID is bound to: the health check looks at every placed
        /// element of these. Empty when the parameter is not in the project yet.
        /// </summary>
        public static HashSet<long> TrackedCategoryIds(Document doc) =>
            new(TrackerParameterService.BoundCategories(doc, TrackerParameters.TrackerId).Where(IsTrackable).Select(c => c.Id.Value));

        // Placed, view-independent elements with a type, within the scope and (when given) the categories.
        private static IEnumerable<Element> PlacedElements(Document doc, ReadScope scope, ISet<long> categoryIds = null)
        {
            scope ??= ReadScope.WholeModel;
            FilteredElementCollector collector;
            switch (scope.Mode)
            {
                case ScopeMode.Selection:
                    var ids = scope.ElementIds.Select(id => new ElementId(id)).Where(id => doc.GetElement(id) != null).ToList();
                    if (ids.Count == 0) return Enumerable.Empty<Element>();
                    collector = new FilteredElementCollector(doc, ids);
                    break;
                case ScopeMode.ActiveView when scope.ViewId != 0 && doc.GetElement(new ElementId(scope.ViewId)) is View:
                    collector = new FilteredElementCollector(doc, new ElementId(scope.ViewId));
                    break;
                default:
                    collector = new FilteredElementCollector(doc);
                    break;
            }

            if (categoryIds != null)
            {
                if (categoryIds.Count == 0) return Enumerable.Empty<Element>();
                if (categoryIds.Count == 1) collector.OfCategoryId(new ElementId(categoryIds.First()));
                else collector.WherePasses(new ElementMulticategoryFilter(categoryIds.Select(id => new ElementId(id)).ToList()));
            }

            return collector.WhereElementIsNotElementType()
                            .WhereElementIsViewIndependent()
                            .Where(e => e.Category != null && e.GetTypeId() != ElementId.InvalidElementId);
        }

        private static IEnumerable<Element> ElementsWithTrackerId(Document doc)
        {
            var (definition, _) = TrackerParameterService.FindBinding(doc, TrackerParameters.TrackerId);
            if (definition is not InternalDefinition internalDefinition) return Enumerable.Empty<Element>();

            var rule = ParameterFilterRuleFactory.CreateHasValueParameterRule(internalDefinition.Id);
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementParameterFilter(rule));
        }

        private static bool HasTrackerId(Element e) => Text(e, TrackerParameters.TrackerId).Length > 0;

        private static string Text(Element e, string name) => e.LookupParameter(name)?.AsString()?.Trim() ?? string.Empty;

        // A number parameter's value, or a number typed into a Text parameter of that name; null when empty.
        private static double? Number(Element e, string name)
        {
            var p = e.LookupParameter(name);
            if (p == null || !p.HasValue) return null;
            if (p.StorageType == StorageType.Double) return p.AsDouble();
            if (p.StorageType == StorageType.Integer) return p.AsInteger();
            return double.TryParse(p.AsString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
        }

        // A parameter's value as text: strings as stored, anything else as Revit displays it. Null when there is no parameter.
        private static string ParameterText(Parameter p)
        {
            if (p == null) return null;
            if (!p.HasValue) return string.Empty;
            return p.StorageType == StorageType.String ? p.AsString() ?? string.Empty : p.AsValueString() ?? string.Empty;
        }

        private static string TypeMark(ElementType type)
        {
            if (type == null) return string.Empty;
            string mark = type.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK)?.AsString();
            if (string.IsNullOrWhiteSpace(mark)) mark = type.get_Parameter(BuiltInParameter.WINDOW_TYPE_ID)?.AsString();
            return mark?.Trim() ?? string.Empty;
        }

        // Bounding-box centre; falls back to the location point or the middle of the location curve.
        private static XYZ Center(Element e)
        {
            var bb = e.get_BoundingBox(null);
            if (bb != null) return (bb.Min + bb.Max) / 2;
            return e.Location switch
            {
                LocationPoint lp => lp.Point,
                LocationCurve lc => lc.Curve.Evaluate(0.5, true),
                _ => null
            };
        }

        // The element's own level, else a level parameter, else the highest level at or below its centre.
        private static Level ResolveLevel(Document doc, Element e, List<Level> levelsByElevation, double? z)
        {
            if (e.LevelId != ElementId.InvalidElementId && doc.GetElement(e.LevelId) is Level own) return own;

            foreach (var bip in LevelParameters)
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId && doc.GetElement(p.AsElementId()) is Level byParam)
                    return byParam;
            }

            if (!z.HasValue || levelsByElevation.Count == 0) return null;
            return levelsByElevation.LastOrDefault(l => l.Elevation <= z.Value + 0.01) ?? levelsByElevation[0];
        }

        private static double? Quantity(Element e, ForgeTypeId unit, params BuiltInParameter[] parameters)
        {
            foreach (var bip in parameters)
            {
                var p = e.get_Parameter(bip);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) continue;
                double v = p.AsDouble();
                if (v > 0) return UnitUtils.ConvertFromInternalUnits(v, unit);
            }
            return null;
        }
    }
}
