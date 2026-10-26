using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>The rename rules of the Rename styles tab, applied in this order: find/replace, prefix, suffix, case, trim.</summary>
    public sealed class RenameRules
    {
        /// <summary>Text (or pattern) to find; empty for none.</summary>
        public string Find { get; init; } = string.Empty;

        /// <summary>Replacement for every match.</summary>
        public string Replace { get; init; } = string.Empty;

        /// <summary><see cref="Find"/> is a regular expression (<c>$1</c> works in <see cref="Replace"/>).</summary>
        public bool UseRegex { get; init; }

        /// <summary>Find matches case.</summary>
        public bool MatchCase { get; init; }

        /// <summary>Text added in front.</summary>
        public string Prefix { get; init; } = string.Empty;

        /// <summary>Text added at the end.</summary>
        public string Suffix { get; init; } = string.Empty;

        /// <summary>Letter case.</summary>
        public NameCase Case { get; init; } = NameCase.Unchanged;

        /// <summary>Trim the ends and collapse repeated spaces.</summary>
        public bool Trim { get; init; } = true;

        /// <summary>True when no rule would change any name.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Find) && string.IsNullOrEmpty(Prefix) && string.IsNullOrEmpty(Suffix)
                               && Case == NameCase.Unchanged && !Trim;
    }

    /// <summary>One planned rename: a style, its current name and what the rules make of it.</summary>
    public sealed class RenamePlanItem
    {
        /// <summary>Key the caller uses to find the style again (its subcategory id value).</summary>
        public long Key { get; init; }

        /// <summary>Name now.</summary>
        public string CurrentName { get; init; } = string.Empty;

        /// <summary>True for a built-in style, never renamed.</summary>
        public bool IsBuiltIn { get; init; }

        /// <summary>Name after the rules (and the duplicate rule).</summary>
        public string NewName { get; set; } = string.Empty;

        /// <summary>Result of the checks.</summary>
        public RenameStatus Status { get; set; }

        /// <summary>Why it is not renamed, or a note on the new name; empty when plain.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Works out new line style names and checks them: characters Revit refuses, names already taken
    /// by another style, and two styles that would end up with the same name. Revit compares style
    /// names without case, so this does too. Pure logic, no Revit API, so it can run on the UI thread.
    /// </summary>
    public static class StyleRenamer
    {
        /// <summary>Characters Revit refuses in names.</summary>
        public const string ForbiddenChars = "\\:{}[]|;<>?`~";

        private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

        /// <summary>
        /// Checks <paramref name="rules"/>; returns an error message for an invalid regular expression, or null.
        /// </summary>
        public static string Validate(RenameRules rules)
        {
            if (rules == null || !rules.UseRegex || string.IsNullOrEmpty(rules.Find)) return null;
            try
            {
                _ = new Regex(rules.Find, Options(rules), TimeSpan.FromSeconds(1));
                return null;
            }
            catch (ArgumentException ex)
            {
                return "Invalid regular expression: " + ex.Message;
            }
        }

        /// <summary>Applies <paramref name="rules"/> to <paramref name="name"/>. Throws <see cref="ArgumentException"/> for a bad pattern.</summary>
        public static string Apply(string name, RenameRules rules)
        {
            string s = name ?? string.Empty;
            if (!string.IsNullOrEmpty(rules.Find))
            {
                s = rules.UseRegex
                    ? Regex.Replace(s, rules.Find, rules.Replace ?? string.Empty, Options(rules), TimeSpan.FromSeconds(1))
                    : ReplaceLiteral(s, rules.Find, rules.Replace ?? string.Empty, rules.MatchCase);
            }
            s = (rules.Prefix ?? string.Empty) + s + (rules.Suffix ?? string.Empty);
            s = rules.Case switch
            {
                NameCase.Upper => s.ToUpper(CultureInfo.CurrentCulture),
                NameCase.Lower => s.ToLower(CultureInfo.CurrentCulture),
                NameCase.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower(CultureInfo.CurrentCulture)),
                _ => s
            };
            if (rules.Trim) s = Spaces.Replace(s.Trim(), " ");
            return s;
        }

        /// <summary>Returns why <paramref name="name"/> cannot be a style name, or null when it can.</summary>
        public static string NameProblem(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "the new name is empty";
            int bad = name.IndexOfAny(ForbiddenChars.ToCharArray());
            if (bad >= 0) return $"'{name[bad]}' is not allowed in a name";
            if (name.Any(char.IsControl)) return "the new name has control characters";
            return null;
        }

        /// <summary>
        /// Builds the plan for <paramref name="items"/>: applies the rules, then marks each item
        /// Unchanged, Invalid, Duplicate (name of a style that keeps it, or the same new name as an
        /// earlier item) or WillRename. <paramref name="allNames"/> holds every line style name in the
        /// project, listed or not. With <see cref="DuplicateNameRule.AddNumber"/> a taken name gets
        /// " (2)", " (3)", … instead of being skipped. Items are checked in the order given.
        /// </summary>
        public static void Plan(IList<RenamePlanItem> items, IEnumerable<string> allNames, RenameRules rules, DuplicateNameRule onDuplicate)
        {
            var cmp = StringComparer.CurrentCultureIgnoreCase;
            string patternError = Validate(rules);

            foreach (var it in items)
            {
                it.Reason = string.Empty;
                if (it.IsBuiltIn) { it.NewName = it.CurrentName; it.Status = RenameStatus.BuiltIn; it.Reason = "built-in style"; continue; }
                if (patternError != null) { it.NewName = it.CurrentName; it.Status = RenameStatus.Invalid; it.Reason = patternError; continue; }
                it.NewName = Apply(it.CurrentName, rules);
                if (string.Equals(it.NewName, it.CurrentName, StringComparison.Ordinal)) { it.Status = RenameStatus.Unchanged; continue; }
                string problem = NameProblem(it.NewName);
                it.Status = problem == null ? RenameStatus.WillRename : RenameStatus.Invalid;
                it.Reason = problem ?? string.Empty;
            }

            // Names that stay: every project style not being renamed keeps its name.
            var leaving = new HashSet<string>(items.Where(i => i.Status == RenameStatus.WillRename).Select(i => i.CurrentName), cmp);
            var taken = new HashSet<string>((allNames ?? Enumerable.Empty<string>()).Where(n => !leaving.Contains(n)), cmp);
            var claimedBy = new Dictionary<string, string>(cmp);

            foreach (var it in items.Where(i => i.Status == RenameStatus.WillRename))
            {
                string clash = taken.Contains(it.NewName) ? $"'{it.NewName}' already exists"
                    : claimedBy.TryGetValue(it.NewName, out var other) ? $"same new name as '{other}'"
                    : null;

                if (clash != null && onDuplicate == DuplicateNameRule.AddNumber)
                {
                    string baseName = it.NewName;
                    int n = 2;
                    while (taken.Contains($"{baseName} ({n})") || claimedBy.ContainsKey($"{baseName} ({n})")) n++;
                    it.NewName = $"{baseName} ({n})";
                    it.Reason = clash + $"; numbered ({n})";
                    clash = null;
                }

                if (clash != null)
                {
                    it.Status = RenameStatus.Duplicate;
                    it.Reason = clash;
                    // It keeps its current name, which then counts as taken for the others.
                    taken.Add(it.CurrentName);
                    continue;
                }
                claimedBy[it.NewName] = it.CurrentName;
            }

            // A style marked Duplicate keeps its name; an item that already claimed that name now clashes too.
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var it in items.Where(i => i.Status == RenameStatus.WillRename && taken.Contains(i.NewName)))
                {
                    it.Status = RenameStatus.Duplicate;
                    it.Reason = $"'{it.NewName}' is kept by a style that cannot be renamed";
                    taken.Add(it.CurrentName);
                    changed = true;
                }
            }
        }

        private static RegexOptions Options(RenameRules rules)
            => RegexOptions.CultureInvariant | (rules.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);

        private static string ReplaceLiteral(string s, string find, string replace, bool matchCase)
            => s.Replace(find, replace, matchCase ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase);
    }
}
