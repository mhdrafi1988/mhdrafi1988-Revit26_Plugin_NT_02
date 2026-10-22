using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services
{
    /// <summary>
    /// The filter rail's criteria, applied to rows already collected. Pure .NET; safe on the UI thread.
    /// </summary>
    public sealed class LineFilter
    {
        /// <summary>Plain search text (case-insensitive), used when <see cref="Pattern"/> is null.</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Compiled regular expression, or null for a plain-text search.</summary>
        public Regex Pattern { get; init; }

        /// <summary>Line style names to hide.</summary>
        public HashSet<string> ExcludedStyles { get; init; } = new(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>View type to show, or empty for all.</summary>
        public string ViewType { get; init; } = string.Empty;

        /// <summary>Lowest effective weight shown.</summary>
        public int MinWeight { get; init; } = 1;

        /// <summary>Highest effective weight shown.</summary>
        public int MaxWeight { get; init; } = 16;

        /// <summary>Shortest length in mm, or null.</summary>
        public double? MinLengthMm { get; init; }

        /// <summary>Longest length in mm, or null.</summary>
        public double? MaxLengthMm { get; init; }

        /// <summary>Show straight lines.</summary>
        public bool Lines { get; init; } = true;

        /// <summary>Show arcs.</summary>
        public bool Arcs { get; init; } = true;

        /// <summary>Show splines.</summary>
        public bool Splines { get; init; } = true;

        /// <summary>Show ellipses.</summary>
        public bool Ellipses { get; init; } = true;

        /// <summary>Workset to show, or empty for all.</summary>
        public string Workset { get; init; } = string.Empty;

        /// <summary>Show only duplicates and overlapping lines.</summary>
        public bool OnlyDuplicates { get; init; }

        /// <summary>Show only short lines.</summary>
        public bool OnlyShort { get; init; }

        /// <summary>Show only lines in groups.</summary>
        public bool OnlyInGroups { get; init; }

        /// <summary>Show only lines owned by other users.</summary>
        public bool OnlyOwnedByOthers { get; init; }

        /// <summary>
        /// Compiles <paramref name="text"/> as a case-insensitive regex. Returns null and sets
        /// <paramref name="error"/> when it is not a valid pattern.
        /// </summary>
        public static Regex TryCompile(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>True when <paramref name="row"/> passes every criterion.</summary>
        public bool Matches(DetailLineRow row)
        {
            if (row == null) return false;
            if (OnlyDuplicates && !row.IsDuplicate && !row.IsOverlapping) return false;
            if (OnlyShort && !row.IsShort) return false;
            if (OnlyInGroups && !row.InGroup) return false;
            if (OnlyOwnedByOthers && !row.IsOwnedByOther) return false;

            if (ExcludedStyles.Contains(row.StyleName)) return false;
            if (!string.IsNullOrEmpty(ViewType) && !string.Equals(row.ViewTypeName, ViewType, StringComparison.CurrentCultureIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(Workset) && !string.Equals(row.WorksetName, Workset, StringComparison.CurrentCultureIgnoreCase)) return false;
            if (row.EffectiveWeight < MinWeight || row.EffectiveWeight > MaxWeight) return false;
            if (MinLengthMm.HasValue && row.LengthMm < MinLengthMm.Value) return false;
            if (MaxLengthMm.HasValue && row.LengthMm > MaxLengthMm.Value) return false;

            bool kindOk = row.Curve switch
            {
                CurveKind.Line => Lines,
                CurveKind.Arc => Arcs,
                CurveKind.Spline => Splines,
                CurveKind.Ellipse => Ellipses,
                _ => true
            };
            if (!kindOk) return false;

            return MatchesText(row);
        }

        private bool MatchesText(DetailLineRow row)
        {
            if (Pattern == null && string.IsNullOrWhiteSpace(Text)) return true;

            string id = row.IdValue.ToString(CultureInfo.InvariantCulture);
            string[] fields = { row.StyleName, row.ViewName, id, row.Comment };
            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field)) continue;
                if (Pattern != null)
                {
                    try { if (Pattern.IsMatch(field)) return true; }
                    catch (RegexMatchTimeoutException) { return false; }
                }
                else if (field.IndexOf(Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
