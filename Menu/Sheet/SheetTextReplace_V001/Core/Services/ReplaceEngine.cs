using Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.SheetTextReplace.V001.Core.Services
{
    /// <summary>
    /// The find / replace rules of Sheet Text Replace: plain or regex find, match case, whole word,
    /// trim spaces, prefix and suffix. Pure string functions with no Revit or WPF dependency, so they
    /// can be tested on their own (Tests/SheetTextReplace.Tests). Follows the approach of Bulk Rename's
    /// NameRules (Replace, IsValidRegex) and adds the edit list that lets Revit keep the note's formatting.
    /// </summary>
    public static class ReplaceEngine
    {
        /// <summary>A pattern that runs longer than this on one note is stopped.</summary>
        public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        // Marks around inserted text inside the working string; never part of note text.
        private const char MarkStart = '\u0001';
        private const char MarkEnd = '\u0002';

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
        /// Checks <paramref name="options"/> and builds the regex used by <see cref="Run(string, ReplaceOptions, Regex)"/>.
        /// Returns false with a message for the user when Find is empty or the regex is invalid.
        /// </summary>
        public static bool TryBuildRegex(ReplaceOptions options, out Regex regex, out string error)
        {
            regex = null;
            error = null;
            if (options == null || string.IsNullOrEmpty(options.Find))
            {
                error = "Enter the text to find.";
                return false;
            }

            if (options.UseRegex && !IsValidRegex(options.Find, out string regexError))
            {
                error = "Invalid regular expression: " + regexError;
                return false;
            }

            string pattern = options.UseRegex ? options.Find : Regex.Escape(options.Find);
            if (options.WholeWord)
                pattern = @"(?<!\w)(?:" + pattern + @")(?!\w)";

            var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            try
            {
                regex = new Regex(pattern, flags, RegexTimeout);
                return true;
            }
            catch (ArgumentException ex)
            {
                error = "Invalid regular expression: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Runs the replace on <paramref name="text"/>. Throws <see cref="ArgumentException"/> when the
        /// options are invalid (check with <see cref="TryBuildRegex"/> first).
        /// </summary>
        public static ReplaceOutcome Run(string text, ReplaceOptions options)
        {
            if (!TryBuildRegex(options, out var regex, out string error))
                throw new ArgumentException(error, nameof(options));
            return Run(text, options, regex);
        }

        /// <summary>
        /// Runs the replace on <paramref name="text"/> with a regex from <see cref="TryBuildRegex"/>.
        /// Prefix, suffix and trim are applied only when the text matches. Throws
        /// <see cref="RegexMatchTimeoutException"/> if the pattern takes over a second.
        /// </summary>
        public static ReplaceOutcome Run(string text, ReplaceOptions options, Regex regex)
        {
            text ??= string.Empty;
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (regex == null) throw new ArgumentNullException(nameof(regex));

            var matches = regex.Matches(text);
            if (matches.Count == 0)
            {
                return new ReplaceOutcome
                {
                    Matched = false,
                    OldText = text,
                    NewText = text,
                    OldSegments = Segments(text, SegmentKind.Same),
                    NewSegments = Segments(text, SegmentKind.Same)
                };
            }

            // Pass 1: replace each match.
            var replaceEdits = new List<TextEdit>();
            var oldSegments = new List<TextSegment>();
            var working = new StringBuilder();
            int pos = 0;
            foreach (Match m in matches)
            {
                string replacement = options.UseRegex ? m.Result(options.Replacement ?? string.Empty) : options.Replacement ?? string.Empty;
                string before = text.Substring(pos, m.Index - pos);

                AddSegment(oldSegments, before, SegmentKind.Same);
                AddSegment(oldSegments, m.Value, SegmentKind.Removed);

                working.Append(before);
                if (replacement.Length > 0)
                    working.Append(MarkStart).Append(replacement).Append(MarkEnd);
                if (replacement != m.Value)
                    replaceEdits.Add(new TextEdit(m.Index, m.Length, replacement));

                pos = m.Index + m.Length;
            }
            string tail = text.Substring(pos);
            AddSegment(oldSegments, tail, SegmentKind.Same);
            working.Append(tail);

            string marked = working.ToString();

            // Pass 2: trim spaces.
            var trimEdits = new List<TextEdit>();
            if (options.TrimSpaces)
                marked = TrimMarked(marked, trimEdits);

            // Pass 3: prefix and suffix (suffix goes before a trailing line break).
            var affixEdits = new List<TextEdit>();
            string plain = Strip(marked);
            if (!string.IsNullOrEmpty(options.Prefix))
            {
                affixEdits.Add(new TextEdit(0, 0, options.Prefix));
                marked = MarkStart + options.Prefix + MarkEnd + marked;
            }
            if (!string.IsNullOrEmpty(options.Suffix))
            {
                int trailingBreaks = CountTrailingBreaks(plain);
                affixEdits.Add(new TextEdit(plain.Length - trailingBreaks, 0, options.Suffix));
                int at = marked.Length;
                while (at > 0 && IsBreak(marked[at - 1]))
                    at--;
                marked = marked.Substring(0, at) + MarkStart + options.Suffix + MarkEnd + marked.Substring(at);
            }

            var passes = new List<IReadOnlyList<TextEdit>>();
            if (replaceEdits.Count > 0) passes.Add(replaceEdits);
            if (trimEdits.Count > 0) passes.Add(trimEdits);
            if (affixEdits.Count > 0) passes.Add(affixEdits);

            return new ReplaceOutcome
            {
                Matched = true,
                MatchCount = matches.Count,
                OldText = text,
                NewText = Strip(marked),
                OldSegments = oldSegments,
                NewSegments = ParseMarked(marked),
                EditPasses = passes
            };
        }

        /// <summary>
        /// Applies <paramref name="passes"/> (from <see cref="ReplaceOutcome.EditPasses"/>) to
        /// <paramref name="text"/>. Each pass is applied last edit first, so earlier positions stay valid.
        /// </summary>
        public static string ApplyEdits(string text, IReadOnlyList<IReadOnlyList<TextEdit>> passes)
        {
            text ??= string.Empty;
            if (passes == null) return text;
            foreach (var pass in passes)
            {
                foreach (var edit in pass.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
                    text = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text ?? string.Empty);
            }
            return text;
        }

        /// <summary>True for the characters Revit and Windows use as line breaks.</summary>
        public static bool IsBreak(char c) => c == '\r' || c == '\n' || c == '\u000B' || c == '\u2028' || c == '\u2029';

        private static bool IsSpace(char c) => c == ' ' || c == '\t' || c == ' ';

        /// <summary>
        /// Trim on a working string that carries insertion marks: decides which plain characters go,
        /// records them as deletions (plain positions) and removes them from the marked string.
        /// </summary>
        private static string TrimMarked(string marked, List<TextEdit> edits)
        {
            var plainToMarked = new List<int>(marked.Length);
            var plainBuilder = new StringBuilder(marked.Length);
            for (int i = 0; i < marked.Length; i++)
            {
                if (marked[i] == MarkStart || marked[i] == MarkEnd) continue;
                plainToMarked.Add(i);
                plainBuilder.Append(marked[i]);
            }

            string plain = plainBuilder.ToString();
            var delete = new bool[plain.Length];
            int lineStart = 0;
            while (lineStart <= plain.Length)
            {
                int lineEnd = lineStart;
                while (lineEnd < plain.Length && !IsBreak(plain[lineEnd]))
                    lineEnd++;

                int a = lineStart;
                while (a < lineEnd && IsSpace(plain[a])) delete[a++] = true;
                int b = lineEnd - 1;
                while (b >= a && IsSpace(plain[b])) delete[b--] = true;
                for (int i = a; i <= b; i++)
                {
                    if (!IsSpace(plain[i])) continue;
                    int j = i;
                    while (j + 1 <= b && IsSpace(plain[j + 1])) j++;
                    for (int k = i + 1; k <= j; k++) delete[k] = true;
                    i = j;
                }

                if (lineEnd >= plain.Length) break;
                lineStart = lineEnd + 1;
            }

            for (int i = 0; i < delete.Length; i++)
            {
                if (!delete[i]) continue;
                int j = i;
                while (j + 1 < delete.Length && delete[j + 1]) j++;
                edits.Add(new TextEdit(i, j - i + 1, string.Empty));
                i = j;
            }

            if (edits.Count == 0) return marked;

            var deleteMarked = new HashSet<int>();
            for (int i = 0; i < delete.Length; i++)
                if (delete[i]) deleteMarked.Add(plainToMarked[i]);

            var result = new StringBuilder(marked.Length);
            for (int i = 0; i < marked.Length; i++)
                if (!deleteMarked.Contains(i)) result.Append(marked[i]);
            return result.ToString().Replace(new string(new[] { MarkStart, MarkEnd }), string.Empty);
        }

        private static int CountTrailingBreaks(string s)
        {
            int n = 0;
            while (n < s.Length && IsBreak(s[s.Length - 1 - n])) n++;
            return n;
        }

        private static string Strip(string marked)
            => marked.Replace(MarkStart.ToString(), string.Empty).Replace(MarkEnd.ToString(), string.Empty);

        private static List<TextSegment> ParseMarked(string marked)
        {
            var segments = new List<TextSegment>();
            var current = new StringBuilder();
            var kind = SegmentKind.Same;
            foreach (char c in marked)
            {
                if (c == MarkStart || c == MarkEnd)
                {
                    AddSegment(segments, current.ToString(), kind);
                    current.Clear();
                    kind = c == MarkStart ? SegmentKind.Inserted : SegmentKind.Same;
                }
                else
                {
                    current.Append(c);
                }
            }
            AddSegment(segments, current.ToString(), kind);
            return segments;
        }

        private static List<TextSegment> Segments(string text, SegmentKind kind)
        {
            var list = new List<TextSegment>();
            AddSegment(list, text, kind);
            return list;
        }

        private static void AddSegment(List<TextSegment> list, string text, SegmentKind kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (list.Count > 0 && list[^1].Kind == kind)
                list[^1] = new TextSegment(list[^1].Text + text, kind);
            else
                list.Add(new TextSegment(text, kind));
        }
    }
}
