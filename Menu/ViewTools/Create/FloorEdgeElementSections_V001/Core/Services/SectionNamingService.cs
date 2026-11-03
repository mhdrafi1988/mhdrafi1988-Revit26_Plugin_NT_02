using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Builds section view names from the user-configured token pattern (Zone, Level,
    /// Name, Line of Direction, Category, Area, Number — see NamingToken), in the
    /// order and on/off state stored in FloorEdgeElementSectionsSettings.NamingTokens.
    /// Adapted from FloorEdgeAroundSections_V004's SectionNamingService, with a new
    /// Category token resolved from the matched cluster's category.
    ///
    /// Dedup: no row is ever skipped on a name collision — a D-series suffix
    /// (D1, D2, ...) is appended automatically instead.
    /// </summary>
    public static class SectionNamingService
    {
        public static string GetFloorDisplayName(Element floor)
        {
            string name = floor.Name;
            if (string.IsNullOrWhiteSpace(name) || name == floor.Id.Value.ToString())
                return $"Floor_{floor.Id.Value}";
            return SanitizeForViewName(name);
        }

        /// <summary>
        /// Builds the final, deduplicated section view name from the enabled naming
        /// tokens. existingNames is checked for collisions and MUTATED (the returned
        /// name is added to it) so subsequent calls in the same plan-build pass see
        /// names generated earlier in this run, not just names already in the document.
        /// </summary>
        public static string BuildSectionViewName(
            Floor floor,
            string floorDisplayName,
            EdgeDirection direction,
            string matchedCategoryName,
            FloorEdgeElementSectionsSettings settings,
            int numberInSequence,
            HashSet<string> existingNames,
            out bool wasRenamedForDuplicate)
        {
            string baseName = BuildBaseName(floor, floorDisplayName, direction, matchedCategoryName, settings, numberInSequence);
            string separator = string.IsNullOrEmpty(settings.NamingSeparator) ? "_" : settings.NamingSeparator;

            string finalName = baseName;
            int dupIndex = 0;
            while (existingNames.Contains(finalName))
            {
                dupIndex++;
                finalName = $"{baseName}{separator}D{dupIndex}";
            }

            wasRenamedForDuplicate = dupIndex > 0;
            existingNames.Add(finalName);
            return finalName;
        }

        private static string BuildBaseName(
            Floor floor,
            string floorDisplayName,
            EdgeDirection direction,
            string matchedCategoryName,
            FloorEdgeElementSectionsSettings settings,
            int numberInSequence)
        {
            var parts = new List<string>();

            foreach (NamingToken token in settings.NamingTokens
                         .Where(t => t.IsEnabled)
                         .OrderBy(t => t.Order))
            {
                string value = ResolveToken(token.Type, floor, floorDisplayName, direction, matchedCategoryName, numberInSequence);
                if (!string.IsNullOrWhiteSpace(value))
                    parts.Add(value);
            }

            if (parts.Count == 0)
            {
                parts.Add(floorDisplayName);
                parts.Add(direction.ToString());
            }

            string separator = string.IsNullOrEmpty(settings.NamingSeparator) ? "_" : settings.NamingSeparator;
            string joined = string.Join(separator, parts).Replace(" ", separator);
            return SanitizeForViewName(joined);
        }

        private static string ResolveToken(
            NamingTokenType type,
            Floor floor,
            string floorDisplayName,
            EdgeDirection direction,
            string matchedCategoryName,
            int numberInSequence)
        {
            switch (type)
            {
                case NamingTokenType.Zone:
                    return GetZoneValue(floor);

                case NamingTokenType.Level:
                    return GetLevelName(floor);

                case NamingTokenType.Name:
                    return floorDisplayName;

                case NamingTokenType.LineOfDirection:
                    return $"Line of {direction}";

                case NamingTokenType.Category:
                    return string.IsNullOrWhiteSpace(matchedCategoryName) ? null : SanitizeForViewName(matchedCategoryName);

                case NamingTokenType.Area:
                    return GetAreaValue(floor);

                case NamingTokenType.Number:
                    return numberInSequence.ToString("D2");

                default:
                    return null;
            }
        }

        private static string GetZoneValue(Floor floor)
        {
            Parameter p = floor.LookupParameter("Zone");
            string value = p?.AsString() ?? p?.AsValueString();

            if (string.IsNullOrWhiteSpace(value))
            {
                ElementId typeId = floor.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element floorType = floor.Document.GetElement(typeId);
                    Parameter typeParam = floorType?.LookupParameter("Zone");
                    value = typeParam?.AsString() ?? typeParam?.AsValueString();
                }
            }

            return string.IsNullOrWhiteSpace(value) ? null : SanitizeForViewName(value);
        }

        private static string GetLevelName(Floor floor)
        {
            ElementId levelId = floor.LevelId;
            if (levelId == null || levelId == ElementId.InvalidElementId)
                return null;

            Level level = floor.Document.GetElement(levelId) as Level;
            return level != null ? SanitizeForViewName(level.Name) : null;
        }

        private static string GetAreaValue(Floor floor)
        {
            Parameter areaParam = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            if (areaParam == null)
                return null;

            double areaInternal = areaParam.AsDouble();

            ForgeTypeId unitTypeId = floor.Document.GetUnits()
                .GetFormatOptions(SpecTypeId.Area).GetUnitTypeId();

            bool isMetric = unitTypeId == UnitTypeId.SquareMeters;
            double displayArea = isMetric
                ? UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareMeters)
                : UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareFeet);

            string suffix = isMetric ? "m2" : "ft2";
            return $"{displayArea:F0}{suffix}";
        }

        public static HashSet<string> GetExistingViewNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name)
                .ToHashSet();
        }

        private static string SanitizeForViewName(string raw)
        {
            char[] invalid = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
            foreach (char c in invalid)
                raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
