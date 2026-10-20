#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Models;

namespace Revit26_Plugin_Revised.RoofNameBuilder.V002.Core.Services
{
    /// <summary>Result of splitting an existing name into base name + recognised code segments.</summary>
    public sealed class ParsedName
    {
        public string BaseName { get; init; } = string.Empty;

        /// <summary>code -> the value that was found in the name (e.g. "INS" -> "50").</summary>
        public Dictionary<string, string> FoundCodes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One row's input to the duplicate check.</summary>
    public readonly record struct NameCandidate(long ElementId, string FinalName, bool IsPending);

    /// <summary>
    /// Pure naming logic — zero Revit API dependency (CLAUDE.md Core/Services convention:
    /// "testable in principle even though no test project exists yet").
    ///
    /// BuildName always rebuilds deterministically from the ticked codes in Order, so unticking a
    /// code removes exactly its segment. V002 adds ParseName, the inverse, so names that already
    /// carry codes are split back into base + codes on load instead of getting codes appended twice.
    /// </summary>
    public static class NameBuilderService
    {
        // Characters Revit rejects in element/type names.
        private static readonly char[] IllegalCharacters =
            { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        public static string BuildName(RoofNameRow row, IEnumerable<NameCodeDefinition> definitions, string separator)
        {
            var sb = new StringBuilder((row.BaseName ?? string.Empty).Trim());
            separator ??= string.Empty;

            foreach (var def in definitions.OrderBy(d => d.Order))
            {
                if (string.IsNullOrWhiteSpace(def.Code)) continue;
                if (!row.Codes.TryGetValue(def.Code, out var cv) || !cv.Ticked)
                    continue;

                sb.Append(separator);
                sb.Append(BuildSegment(def, cv.Value));
            }

            return sb.ToString();
        }

        /// <summary>The text one ticked code contributes to the name, without the separator.</summary>
        public static string BuildSegment(NameCodeDefinition def, string? value)
        {
            var v = (value ?? string.Empty).Trim();
            if (def.UppercaseValue) v = v.ToUpperInvariant();
            return def.IncludeCodePrefix ? def.Code + v : v;
        }

        /// <summary>
        /// Splits trailing code segments off <paramref name="name"/>. Segments are matched from the end,
        /// in descending Order, so codes must appear in template order (as BuildName writes them).
        /// At least one segment is always left as the base name.
        ///
        /// A prefixed code (INS) matches a segment that starts with the code and continues with nothing,
        /// a number, or exactly the parameter's current value — so a base segment like "INSULATED" is not
        /// mistaken for INS. A non-prefixed code (MAT) can only be recognised by its value, so it matches
        /// only when the segment equals the parameter's current value (case-insensitive).
        /// </summary>
        public static ParsedName ParseName(
            string? name,
            IEnumerable<NameCodeDefinition> definitions,
            string? separator,
            IReadOnlyDictionary<string, string> parameterValues)
        {
            name ??= string.Empty;
            if (string.IsNullOrEmpty(separator) || name.Length == 0)
                return new ParsedName { BaseName = name };

            var segments = name.Split(new[] { separator }, StringSplitOptions.None);
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idx = segments.Length - 1;

            foreach (var def in definitions.Where(d => !string.IsNullOrWhiteSpace(d.Code)).OrderByDescending(d => d.Order))
            {
                if (idx < 1) break; // keep at least one segment as the base

                parameterValues.TryGetValue(def.Code, out var paramValue);
                if (TryMatchSegment(def, segments[idx], paramValue, out var foundValue))
                {
                    found[def.Code] = foundValue;
                    idx--;
                }
            }

            var baseName = string.Join(separator, segments.Take(idx + 1));
            return new ParsedName { BaseName = baseName, FoundCodes = found };
        }

        private static bool TryMatchSegment(NameCodeDefinition def, string segment, string? paramValue, out string value)
        {
            value = string.Empty;
            var param = (paramValue ?? string.Empty).Trim();

            if (def.IncludeCodePrefix)
            {
                if (!segment.StartsWith(def.Code, StringComparison.OrdinalIgnoreCase)) return false;
                var rest = segment.Substring(def.Code.Length);

                var ok = rest.Length == 0
                         || char.IsDigit(rest[0]) || rest[0] == '-' || rest[0] == '.' || rest[0] == '+'
                         || (param.Length > 0 && string.Equals(rest, param, StringComparison.OrdinalIgnoreCase));
                if (!ok) return false;

                value = rest;
                return true;
            }

            if (param.Length == 0) return false;
            if (!string.Equals(segment, param, StringComparison.OrdinalIgnoreCase)) return false;

            value = segment;
            return true;
        }

        public static IReadOnlyList<char> FindIllegalCharacters(string? name)
        {
            if (string.IsNullOrEmpty(name)) return Array.Empty<char>();
            return IllegalCharacters.Where(name.Contains).Distinct().ToList();
        }

        public static bool IsNameTooLong(string? name, int threshold) =>
            !string.IsNullOrEmpty(name) && name.Length > threshold;

        /// <summary>
        /// Ids of PENDING rows whose final name collides with another row's final name, or with a
        /// reserved name that exists in the model outside the loaded rows (e.g. a roof type not in the
        /// current scope). A row's final name is its new name if pending, else its current name.
        /// Empty names are ignored — in Instances mode most roofs start with a blank Mark.
        /// Only pending rows are flagged: an existing clash the user isn't changing shouldn't block Run.
        /// </summary>
        public static HashSet<long> FindConflictingRowIds(
            IEnumerable<NameCandidate> rows,
            IEnumerable<string>? reservedNames,
            bool caseInsensitive)
        {
            var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var reserved = new HashSet<string>(reservedNames ?? Enumerable.Empty<string>(), comparer);
            var list = rows.Where(r => !string.IsNullOrWhiteSpace(r.FinalName)).ToList();

            var result = new HashSet<long>();
            foreach (var group in list.GroupBy(r => r.FinalName.Trim(), comparer))
            {
                var collides = group.Count() > 1 || reserved.Contains(group.Key);
                if (!collides) continue;

                foreach (var r in group.Where(r => r.IsPending))
                    result.Add(r.ElementId);
            }
            return result;
        }
    }
}
