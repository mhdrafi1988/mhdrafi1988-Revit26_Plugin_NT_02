using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Sections
{
    /// <summary>
    /// A drafting view as the "Points to" search sees it: its name, its Title on Sheet and, when it is
    /// placed on a sheet, the sheet and the bubble (detail number). No Revit types.
    /// </summary>
    /// <param name="Id">ElementId value of the view.</param>
    /// <param name="Name">View name (unique in the project).</param>
    /// <param name="TitleOnSheet">The view's "Title on Sheet"; empty when not set.</param>
    /// <param name="SheetNumber">Number of the sheet the view is placed on; empty when it is on no sheet.</param>
    /// <param name="SheetName">Name of that sheet; empty when it is on no sheet.</param>
    /// <param name="DetailNumber">The bubble number of its viewport; empty when it is on no sheet.</param>
    public sealed record DraftingViewInfo(long Id, string Name, string TitleOnSheet, string SheetNumber, string SheetName, string DetailNumber)
    {
        /// <summary>True when the view is placed on a sheet.</summary>
        public bool IsOnSheet => !string.IsNullOrEmpty(SheetNumber);

        /// <summary>"A-501 / 3" for a placed view, empty otherwise.</summary>
        public string Reference => IsOnSheet ? $"{SheetNumber} / {DetailNumber}" : string.Empty;

        /// <summary>Short placement text: "A-501 / 3 · Wet area details" or "Not on a sheet".</summary>
        public string PlacementText => IsOnSheet
            ? (string.IsNullOrEmpty(SheetName) ? Reference : $"{Reference} · {SheetName}")
            : "Not on a sheet";

        /// <summary>Second line in the result lists: title on sheet and placement.</summary>
        public string Detail => string.IsNullOrEmpty(TitleOnSheet) ? PlacementText : $"{TitleOnSheet} · {PlacementText}";
    }

    /// <summary>Where the picker panel looks.</summary>
    public enum DraftingViewScope
    {
        /// <summary>Every drafting view.</summary>
        All,
        /// <summary>Only views placed on a sheet.</summary>
        OnSheet,
        /// <summary>Only views on no sheet.</summary>
        NotOnSheet
    }

    /// <summary>
    /// Finds drafting views by what the user types: view name, Title on Sheet, sheet number, sheet name or
    /// bubble (detail) number. Words are ANDed. <c>A-501/3</c> means "sheet A-501, bubble 3". Prefixes narrow
    /// a word to one field: <c>s:</c> sheet number, <c>d:</c> bubble, <c>n:</c> view name, <c>t:</c> title on sheet.
    /// </summary>
    public static class DraftingViewSearch
    {
        private static readonly Regex ReferenceForm = new(@"^\s*(?<sheet>[^/]+?)\s*/\s*(?<detail>[^/\s]+)\s*$", RegexOptions.Compiled);
        private static readonly StringComparison Ci = StringComparison.OrdinalIgnoreCase;

        /// <summary>
        /// The views matching <paramref name="query"/> within <paramref name="scope"/>, best first: exact name,
        /// then exact bubble or sheet, then names starting with the query, then the rest by sheet and bubble.
        /// An empty query returns every view in the scope. At most <paramref name="max"/> results (0 = no limit).
        /// </summary>
        public static IReadOnlyList<DraftingViewInfo> Filter(IEnumerable<DraftingViewInfo> views, string query,
            DraftingViewScope scope = DraftingViewScope.All, int max = 0)
        {
            if (views == null) return Array.Empty<DraftingViewInfo>();
            string q = (query ?? string.Empty).Trim();
            List<string> words = Words(q);
            IEnumerable<DraftingViewInfo> hits = views
                .Where(v => v != null && InScope(v, scope) && words.All(w => WordMatches(v, w)))
                .OrderBy(v => Score(v, q, words))
                .ThenBy(v => v.IsOnSheet ? 0 : 1)
                .ThenBy(v => v.SheetNumber ?? string.Empty, NaturalComparer.Instance)
                .ThenBy(v => v.DetailNumber ?? string.Empty, NaturalComparer.Instance)
                .ThenBy(v => v.Name ?? string.Empty, NaturalComparer.Instance);
            return (max > 0 ? hits.Take(max) : hits).ToList();
        }

        /// <summary>The view named exactly <paramref name="name"/> (case ignored), or null.</summary>
        public static DraftingViewInfo FindByName(IEnumerable<DraftingViewInfo> views, string name)
        {
            string n = (name ?? string.Empty).Trim();
            return n.Length == 0 || views == null ? null : views.FirstOrDefault(v => v != null && string.Equals(v.Name, n, Ci));
        }

        /// <summary>
        /// When <paramref name="text"/> is a sheet / bubble reference such as <c>A-501/3</c> that names exactly one
        /// placed view, returns that view; otherwise null. A text that is also a view's name is left alone.
        /// </summary>
        public static DraftingViewInfo ResolveReference(IEnumerable<DraftingViewInfo> views, string text)
        {
            if (views == null || string.IsNullOrWhiteSpace(text)) return null;
            List<DraftingViewInfo> list = views.Where(v => v != null).ToList();
            if (FindByName(list, text) != null) return null;
            Match m = ReferenceForm.Match(text);
            if (!m.Success) return null;
            string sheet = m.Groups["sheet"].Value, detail = m.Groups["detail"].Value;
            List<DraftingViewInfo> hits = list.Where(v => v.IsOnSheet
                && string.Equals(v.SheetNumber, sheet, Ci) && string.Equals(v.DetailNumber, detail, Ci)).ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        /// <summary>
        /// What the "Points to" text will do at the run: "A-501 / 3 · Wet area details" for an existing placed view,
        /// "Not on a sheet" for an existing unplaced one, "New view: created at the run" for a name not in the project.
        /// </summary>
        public static string Describe(IEnumerable<DraftingViewInfo> views, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Empty: the default name is used";
            DraftingViewInfo v = FindByName(views, name);
            return v == null ? "New view: created at the run" : v.PlacementText;
        }

        private static bool InScope(DraftingViewInfo v, DraftingViewScope scope) => scope switch
        {
            DraftingViewScope.OnSheet => v.IsOnSheet,
            DraftingViewScope.NotOnSheet => !v.IsOnSheet,
            _ => true
        };

        /// <summary>Splits on spaces, but keeps "A-501 / 3" together as one reference word.</summary>
        private static List<string> Words(string q)
        {
            if (q.Length == 0) return new List<string>();
            string joined = Regex.Replace(q, @"\s*/\s*", "/");
            return joined.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool WordMatches(DraftingViewInfo v, string word)
        {
            int colon = word.IndexOf(':');
            if (colon == 1)
            {
                string value = word.Substring(2);
                switch (char.ToLowerInvariant(word[0]))
                {
                    case 's': return Has(v.SheetNumber, value) || Has(v.SheetName, value);
                    case 'd': return value.Length == 0 ? v.IsOnSheet : string.Equals(v.DetailNumber, value, Ci);
                    case 'n': return Has(v.Name, value);
                    case 't': return Has(v.TitleOnSheet, value);
                }
            }

            int slash = word.IndexOf('/');
            if (slash >= 0 && v.IsOnSheet)
            {
                string sheet = word.Substring(0, slash), detail = word.Substring(slash + 1);
                bool sheetOk = sheet.Length == 0 || Has(v.SheetNumber, sheet);
                bool detailOk = detail.Length == 0 || string.Equals(v.DetailNumber, detail, Ci);
                if (sheetOk && detailOk) return true;
            }

            return Has(v.Name, word) || Has(v.TitleOnSheet, word) || Has(v.SheetNumber, word)
                || Has(v.SheetName, word) || Has(v.DetailNumber, word);
        }

        private static int Score(DraftingViewInfo v, string q, List<string> words)
        {
            if (string.Equals(v.Name, q, Ci)) return 0;
            if (words.Any(w => string.Equals(v.DetailNumber, w, Ci) || string.Equals(v.SheetNumber, w, Ci)
                || (w.Contains('/') && v.IsOnSheet && string.Equals(v.SheetNumber + "/" + v.DetailNumber, w, Ci)))) return 1;
            if (q.Length > 0 && (v.Name ?? string.Empty).StartsWith(q, Ci)) return 2;
            return 3;
        }

        private static bool Has(string field, string value)
            => !string.IsNullOrEmpty(field) && field.IndexOf(value, Ci) >= 0;
    }

    /// <summary>Orders "A-2" before "A-10" and "3" before "12" (numbers inside text compared as numbers).</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        /// <summary>The shared instance.</summary>
        public static readonly NaturalComparer Instance = new();

        /// <summary>Compares two texts with the numbers in them compared by value, case ignored.</summary>
        public int Compare(string a, string b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                    if (c != 0) return c;
                }
                else
                {
                    int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
