using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Revit26_Plugin.BulkRename.V001.Core.Services
{
    /// <summary>
    /// The text rules of Bulk Rename: find/replace (plain or regex), prefix/suffix, space cleanup,
    /// case change and numbering. Pure string functions with no Revit or WPF dependency, so they
    /// can be tested on their own. Every method returns the new name and never returns null.
    /// </summary>
    public static class NameRules
    {
        /// <summary>Token in a numbering pattern that is replaced by the running number.</summary>
        public const string NumberToken = "{n}";

        /// <summary>Token in a numbering pattern that is replaced by the row's current new name.</summary>
        public const string NameToken = "{name}";

        /// <summary>Characters Revit does not allow in element names.</summary>
        public static readonly char[] ForbiddenChars =
            { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        /// <summary>Returns the first character of <paramref name="name"/> Revit does not allow, or null.</summary>
        public static char? FindForbiddenChar(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int i = name.IndexOfAny(ForbiddenChars);
            return i >= 0 ? name[i] : null;
        }

        /// <summary>True when <paramref name="pattern"/> compiles as a regular expression.</summary>
        public static bool IsValidRegex(string pattern, out string error)
        {
            error = null;
            try
            {
                _ = new Regex(pattern ?? string.Empty, RegexOptions.None, RegexTimeout);
                return true;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Replaces <paramref name="find"/> with <paramref name="replacement"/>. With
        /// <paramref name="useRegex"/> the find text is a regular expression and the replacement may use
        /// $1-style groups. An empty find text changes nothing. Throws <see cref="ArgumentException"/>
        /// for an invalid pattern and <see cref="RegexMatchTimeoutException"/> if a pattern takes over a second.
        /// </summary>
        public static string Replace(string name, string find, string replacement, bool matchCase, bool useRegex)
        {
            name ??= string.Empty;
            if (string.IsNullOrEmpty(find)) return name;
            replacement ??= string.Empty;

            if (useRegex)
            {
                var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.Replace(name, find, replacement, options, RegexTimeout);
            }

            return name.Replace(find, replacement, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Adds <paramref name="prefix"/> unless the name already starts with it.</summary>
        public static string AddPrefix(string name, string prefix)
        {
            name ??= string.Empty;
            return string.IsNullOrEmpty(prefix) || name.StartsWith(prefix, StringComparison.Ordinal) ? name : prefix + name;
        }

        /// <summary>Removes <paramref name="prefix"/> when the name starts with it.</summary>
        public static string RemovePrefix(string name, string prefix)
        {
            name ??= string.Empty;
            return !string.IsNullOrEmpty(prefix) && name.StartsWith(prefix, StringComparison.Ordinal)
                ? name.Substring(prefix.Length)
                : name;
        }

        /// <summary>Adds <paramref name="suffix"/> unless the name already ends with it.</summary>
        public static string AddSuffix(string name, string suffix)
        {
            name ??= string.Empty;
            return string.IsNullOrEmpty(suffix) || name.EndsWith(suffix, StringComparison.Ordinal) ? name : name + suffix;
        }

        /// <summary>Removes <paramref name="suffix"/> when the name ends with it.</summary>
        public static string RemoveSuffix(string name, string suffix)
        {
            name ??= string.Empty;
            return !string.IsNullOrEmpty(suffix) && name.EndsWith(suffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - suffix.Length)
                : name;
        }

        /// <summary>Trims the name and collapses every run of whitespace to a single space.</summary>
        public static string CleanSpaces(string name)
            => Whitespace.Replace((name ?? string.Empty).Trim(), " ");

        /// <summary>Trims the name and turns every run of whitespace into one underscore.</summary>
        public static string SpacesToUnderscores(string name)
            => Whitespace.Replace((name ?? string.Empty).Trim(), "_");

        /// <summary>Upper-cases the name using the current culture.</summary>
        public static string ToUpper(string name) => (name ?? string.Empty).ToUpper(CultureInfo.CurrentCulture);

        /// <summary>Lower-cases the name using the current culture.</summary>
        public static string ToLower(string name) => (name ?? string.Empty).ToLower(CultureInfo.CurrentCulture);

        /// <summary>Title-cases the name: the first letter of each word is a capital, the rest lower case.</summary>
        public static string ToTitle(string name)
            => CultureInfo.CurrentCulture.TextInfo.ToTitleCase((name ?? string.Empty).ToLower(CultureInfo.CurrentCulture));

        /// <summary>True when <paramref name="pattern"/> contains the <see cref="NumberToken"/>.</summary>
        public static bool PatternHasNumber(string pattern)
            => pattern != null && pattern.Contains(NumberToken, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Builds a name from a numbering pattern: <c>{n}</c> becomes <paramref name="value"/> padded with
        /// zeros to <paramref name="digits"/> digits (1 to 9), and <c>{name}</c> becomes <paramref name="name"/>.
        /// </summary>
        public static string Number(string pattern, string name, int value, int digits)
        {
            digits = Math.Max(1, Math.Min(9, digits));
            string number = value.ToString("D" + digits, CultureInfo.InvariantCulture);

            // The number goes in first and the name last, so the name's own text is never re-scanned for tokens.
            return (pattern ?? string.Empty)
                .Replace(NumberToken, number, StringComparison.OrdinalIgnoreCase)
                .Replace(NameToken, name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
