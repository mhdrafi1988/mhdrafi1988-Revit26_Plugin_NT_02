using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.Core.Services
{
    /// <summary>
    /// Decides, for every chosen element, whether its Tracker_ID is kept or a new one is made, and what
    /// the new one is. Pure logic: it reads snapshots and writes nothing, so the window can re-plan on
    /// every keystroke.
    /// <para>Rules:</para>
    /// <list type="bullet">
    /// <item>An existing ID is never changed. A moved element keeps its ID; only the location parameters follow it.</item>
    /// <item>An element whose ID belongs to another element (a copy-paste) gets a new ID. The original is the
    /// element whose Tracker_SourceGuid is its own UniqueId; without one, the lowest element id wins.</item>
    /// <item>New IDs are unique across the whole model, compared without regard to case because Power BI
    /// joins keys that way. A running number counts up per prefix and skips numbers already in use.</item>
    /// </list>
    /// </summary>
    public static class TrackerIdPlanner
    {
        /// <summary>
        /// Plans <paramref name="elements"/> under <paramref name="format"/>.
        /// <paramref name="modelIds"/> lists every element in the model that has a Tracker_ID.
        /// </summary>
        public static List<PlanRow> Plan(IReadOnlyList<ElementSnapshot> elements, TrackerFormat format,
            GridLocator grids, IReadOnlyList<TrackerIdHolder> modelIds)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            if (format == null) throw new ArgumentNullException(nameof(format));

            var holdersById = (modelIds ?? Array.Empty<TrackerIdHolder>())
                .Where(h => h.TrackerId.Length > 0)
                .GroupBy(h => h.TrackerId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(holdersById.Keys, StringComparer.OrdinalIgnoreCase);

            var rows = elements.Select(e => Locate(e, format, grids)).ToList();

            foreach (var row in rows)
                Classify(row, holdersById);

            AssignNewIds(rows.Where(r => r.Action == PlanAction.Create || r.Action == PlanAction.Copy)
                             .OrderBy(r => r.Element.ElementId)
                             .ToList(),
                         format, grids, used);
            return rows;
        }

        /// <summary>
        /// The value <paramref name="segment"/> gives for <paramref name="element"/>, cleaned for use in an
        /// ID. <paramref name="runningNumber"/> fills a running-number segment; null shows "#" placeholders.
        /// </summary>
        public static string SegmentValue(SegmentDefinition segment, ElementSnapshot element, TrackerFormat format,
            GridLocator grids, int? runningNumber)
        {
            string raw;
            switch (segment.Kind)
            {
                case SegmentKind.Zone:
                    raw = ZoneValue(segment, element);
                    return Fallback(raw, format.NoZoneText, format.Separator);
                case SegmentKind.Level:
                    raw = LevelValue(segment, element);
                    break;
                case SegmentKind.Type:
                    if (format.TypeCodes != null && format.TypeCodes.TryGetValue(element.TypeId, out string code) && !string.IsNullOrWhiteSpace(code))
                    {
                        raw = code;
                        break;
                    }
                    raw = segment.Source switch
                    {
                        SegmentSource.TypeName => element.TypeName,
                        SegmentSource.FamilyName => element.FamilyName,
                        _ => element.TypeMark
                    };
                    break;
                case SegmentKind.Grid:
                    raw = element.HasCenter ? grids?.Find(element.CenterX, element.CenterY, segment.Source)?.Label : null;
                    break;
                case SegmentKind.Model:
                    raw = ModelCode(segment, element.Model, out _);
                    break;
                default:
                    raw = segment.Source switch
                    {
                        SegmentSource.FixedText => segment.Option,
                        SegmentSource.ParameterValue => Lookup(element.ParameterValues, segment.Option),
                        _ => runningNumber.HasValue
                            ? runningNumber.Value.ToString(CultureInfo.InvariantCulture).PadLeft(SegmentCatalog.Digits(segment.Option), '0')
                            : new string('#', SegmentCatalog.Digits(segment.Option))
                    };
                    break;
            }
            return Fallback(raw, format.MissingText, format.Separator);
        }

        /// <summary>The full ID for <paramref name="element"/>, joining every enabled segment.</summary>
        public static string BuildId(ElementSnapshot element, TrackerFormat format, GridLocator grids, int? runningNumber)
        {
            string id = string.Join(format.Separator ?? string.Empty,
                format.Segments.Where(s => s.Enabled).Select(s => SegmentValue(s, element, format, grids, runningNumber)));
            return format.UpperCase ? id.ToUpperInvariant() : id;
        }

        /// <summary>
        /// The model code under <paramref name="segment"/>'s source: the Project Information parameter
        /// named in its Option, the discipline code found in the file name, or (Auto) the first of the two
        /// that gives a value. <paramref name="fromFileNameGuess"/> is true when the code is the file
        /// name's words because no known discipline code was found; such a code may not be unique.
        /// </summary>
        public static string ModelCode(SegmentDefinition segment, ModelIdentity model, out bool fromFileNameGuess)
        {
            fromFileNameGuess = false;
            if (model == null || segment == null) return string.Empty;

            string fromProject = Lookup(model.ProjectParameterValues, segment.Option);
            if (segment.Source == SegmentSource.ModelProjectParameter) return fromProject ?? string.Empty;
            if (segment.Source == SegmentSource.ModelAuto && !string.IsNullOrWhiteSpace(fromProject)) return fromProject;

            string fromName = NameRules.ModelCodeFromTitle(model.Title, out bool recognised);
            fromFileNameGuess = !recognised;
            return fromName;
        }

        // ── Location ──────────────────────────────────────────────────────────

        private static PlanRow Locate(ElementSnapshot e, TrackerFormat format, GridLocator grids)
        {
            var zoneSeg = format.Get(SegmentKind.Zone) ?? new SegmentDefinition { Kind = SegmentKind.Zone, Source = SegmentSource.ZoneParameterThenScopeBox, Option = "Zone" };
            var levelSeg = format.Get(SegmentKind.Level) ?? new SegmentDefinition { Kind = SegmentKind.Level, Source = SegmentSource.LevelShortCode };
            var gridSeg = format.Get(SegmentKind.Grid) ?? new SegmentDefinition { Kind = SegmentKind.Grid, Source = SegmentSource.GridCrossing };

            var row = new PlanRow
            {
                Element = e,
                Zone = SegmentValue(zoneSeg, e, format, grids, null),
                Level = SegmentValue(levelSeg, e, format, grids, null)
            };

            var hit = e.HasCenter ? grids?.Find(e.CenterX, e.CenterY, gridSeg.Source) : null;
            row.Grid = Fallback(hit?.Label, format.MissingText, format.Separator);
            row.GridDistanceM = hit?.DistanceMetres;
            row.GridCheck = hit != null && hit.DistanceMetres > format.GridToleranceM;
            return row;
        }

        private static string ZoneValue(SegmentDefinition segment, ElementSnapshot e) => segment.Source switch
        {
            SegmentSource.ScopeBox => e.ScopeBoxName,
            SegmentSource.ZoneParameter => Lookup(e.ParameterValues, segment.Option),
            _ => FirstNonEmpty(Lookup(e.ParameterValues, segment.Option), e.ScopeBoxName)
        };

        private static string LevelValue(SegmentDefinition segment, ElementSnapshot e) => segment.Source switch
        {
            SegmentSource.LevelName => e.LevelName,
            SegmentSource.LevelElevation => e.LevelElevationMm.HasValue
                ? (e.LevelElevationMm.Value >= 0 ? "+" : "-") + Math.Abs(Math.Round(e.LevelElevationMm.Value)).ToString("0", CultureInfo.InvariantCulture)
                : null,
            SegmentSource.LevelParameter => Lookup(e.LevelParameterValues, segment.Option),
            _ => NameRules.LevelShortCode(e.LevelName)
        };

        // ── Keep / copy / create ──────────────────────────────────────────────

        private static void Classify(PlanRow row, Dictionary<string, List<TrackerIdHolder>> holdersById)
        {
            var e = row.Element;
            row.ResultId = e.TrackerId;

            if (!string.IsNullOrEmpty(e.LockReason))
            {
                row.Action = PlanAction.Locked;
                row.Note = e.LockReason;
                return;
            }

            if (string.IsNullOrWhiteSpace(e.TrackerId))
            {
                row.Action = PlanAction.Create;
                row.Note = "No Tracker_ID yet.";
                return;
            }

            if (!holdersById.TryGetValue(e.TrackerId, out var holders))
                holders = new List<TrackerIdHolder>();
            var others = holders.Where(h => h.ElementId != e.ElementId).ToList();

            bool isOriginal = e.SourceGuid.Length > 0 && e.SourceGuid == e.UniqueId;
            if (isOriginal)
            {
                row.Action = PlanAction.Keep;
            }
            else if (others.Count == 0)
            {
                // Nobody else has this ID: an ID written before Tracker_SourceGuid existed, or one whose
                // original was deleted. Keep it and stamp this element as its owner.
                row.Action = PlanAction.Adopt;
                row.Note = "ID kept; this element is stamped as its owner.";
            }
            else
            {
                // Someone else has the same ID. The owner is, in order: an element stamped as the original,
                // the element whose UniqueId this one carries, or else the lowest element id.
                var owner = others.FirstOrDefault(h => h.IsOriginal)
                            ?? others.FirstOrDefault(h => e.SourceGuid.Length > 0 && h.UniqueId == e.SourceGuid);
                if (owner == null)
                {
                    var lowest = others.OrderBy(h => h.ElementId).First();
                    if (lowest.ElementId < e.ElementId) owner = lowest;
                }

                if (owner != null)
                {
                    row.Action = PlanAction.Copy;
                    row.Note = $"Carries the Tracker_ID of element {owner.ElementId} (copied). Gets a new ID.";
                    return;
                }

                row.Action = PlanAction.Adopt;
                row.Note = "ID shared with copies; this element keeps it as the original.";
            }

            if (Moved(e.StoredZone, row.Zone) || Moved(e.StoredLevel, row.Level) || Moved(e.StoredGrid, row.Grid))
            {
                string moved = $"Moved since its ID was written (was {Describe(e.StoredZone, e.StoredLevel, e.StoredGrid)}). ID kept; location parameters update.";
                if (row.Action == PlanAction.Keep) row.Action = PlanAction.KeepMoved;
                row.Note = string.IsNullOrEmpty(row.Note) ? moved : row.Note + " " + moved;
            }
        }

        private static void AssignNewIds(List<PlanRow> rows, TrackerFormat format, GridLocator grids, HashSet<string> used)
        {
            var enabled = format.Segments.Where(s => s.Enabled).ToList();
            var running = enabled.FirstOrDefault(s => s.Source == SegmentSource.RunningNumber);
            var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (enabled.Count == 0)
                {
                    row.ResultId = string.Empty;
                    row.Action = PlanAction.Duplicate;
                    row.Note = "Turn on at least one segment.";
                    continue;
                }

                string id;
                if (running == null)
                {
                    id = BuildId(row.Element, format, grids, null);
                    if (used.Contains(id))
                    {
                        row.ResultId = id;
                        row.Action = PlanAction.Duplicate;
                        row.Note = "This ID is already used. Add a Running number segment or more segments.";
                        continue;
                    }
                }
                else
                {
                    // Every other segment's value forms the prefix the running number counts within.
                    string prefix = string.Join("|", enabled.Where(s => s != running)
                        .Select(s => SegmentValue(s, row.Element, format, grids, null)));
                    counters.TryGetValue(prefix, out int n);
                    do
                    {
                        n++;
                        id = BuildId(row.Element, format, grids, n);
                    } while (used.Contains(id));
                    counters[prefix] = n;
                }

                used.Add(id);
                row.ResultId = id;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// True when the element's stored Tracker_Zone / Level / Grid (written at the last run) differ from
        /// the location <paramref name="row"/> computed now. Empty stored values never count as moved.
        /// </summary>
        public static bool IsMoved(PlanRow row) =>
            row?.Element != null &&
            (Moved(row.Element.StoredZone, row.Zone) || Moved(row.Element.StoredLevel, row.Level) || Moved(row.Element.StoredGrid, row.Grid));

        private static bool Moved(string stored, string current) =>
            !string.IsNullOrWhiteSpace(stored) && !string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);

        private static string Describe(string zone, string level, string grid) =>
            string.Join(" / ", new[] { zone, level, grid }.Where(v => !string.IsNullOrWhiteSpace(v)));

        private static string Lookup(Dictionary<string, string> values, string name) =>
            !string.IsNullOrWhiteSpace(name) && values != null && values.TryGetValue(name.Trim(), out var v) ? v : null;

        private static string FirstNonEmpty(string a, string b) => !string.IsNullOrWhiteSpace(a) ? a : b;

        private static string Fallback(string raw, string missing, string separator)
        {
            string cleaned = NameRules.Clean(raw, separator);
            return cleaned.Length > 0 ? cleaned : NameRules.Clean(missing, separator);
        }
    }
}
