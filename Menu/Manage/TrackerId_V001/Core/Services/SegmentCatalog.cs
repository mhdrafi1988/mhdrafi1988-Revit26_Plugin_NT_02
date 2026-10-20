using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>
    /// Labels, allowed sources and defaults for every segment kind. The window and the settings loader
    /// both read from here, so a saved format can never hold a source its segment does not offer.
    /// </summary>
    public static class SegmentCatalog
    {
        private static readonly Dictionary<SegmentKind, SegmentSource[]> Sources = new()
        {
            [SegmentKind.Zone] = new[] { SegmentSource.ZoneParameterThenScopeBox, SegmentSource.ScopeBox, SegmentSource.ZoneParameter },
            [SegmentKind.Level] = new[] { SegmentSource.LevelShortCode, SegmentSource.LevelName, SegmentSource.LevelElevation, SegmentSource.LevelParameter },
            [SegmentKind.Type] = new[] { SegmentSource.TypeMark, SegmentSource.TypeName, SegmentSource.FamilyName },
            [SegmentKind.Grid] = new[] { SegmentSource.GridCrossing, SegmentSource.GridLetter, SegmentSource.GridNumber },
            [SegmentKind.Custom1] = CustomSources(),
            [SegmentKind.Custom2] = CustomSources(),
            [SegmentKind.Custom3] = CustomSources(),
            [SegmentKind.Model] = new[] { SegmentSource.ModelAuto, SegmentSource.ModelProjectParameter, SegmentSource.ModelFileName }
        };

        private static SegmentSource[] CustomSources() =>
            new[] { SegmentSource.FixedText, SegmentSource.ParameterValue, SegmentSource.RunningNumber };

        /// <summary>Display name of a segment.</summary>
        public static string Label(SegmentKind kind) => kind switch
        {
            SegmentKind.Custom1 => "Custom 1",
            SegmentKind.Custom2 => "Custom 2",
            SegmentKind.Custom3 => "Custom 3",
            _ => kind.ToString()
        };

        /// <summary>One-line description shown under the segment name.</summary>
        public static string Note(SegmentKind kind) => kind switch
        {
            SegmentKind.Zone => "where it sits",
            SegmentKind.Level => "host or reference level",
            SegmentKind.Type => "element type",
            SegmentKind.Grid => "from centre point",
            SegmentKind.Model => "this Revit file",
            _ => "your choice"
        };

        /// <summary>The sources <paramref name="kind"/> can use, in display order.</summary>
        public static IReadOnlyList<SegmentSource> SourcesFor(SegmentKind kind) => Sources[kind];

        /// <summary>Display name of a source.</summary>
        public static string SourceLabel(SegmentSource source) => source switch
        {
            SegmentSource.ZoneParameterThenScopeBox => "Zone parameter, else scope box",
            SegmentSource.ScopeBox => "Scope box only",
            SegmentSource.ZoneParameter => "Zone parameter only",
            SegmentSource.LevelShortCode => "Level short code (L01)",
            SegmentSource.LevelName => "Level name",
            SegmentSource.LevelElevation => "Level elevation (mm)",
            SegmentSource.LevelParameter => "Level parameter",
            SegmentSource.TypeMark => "Type Mark",
            SegmentSource.TypeName => "Type name",
            SegmentSource.FamilyName => "Family name",
            SegmentSource.GridCrossing => "Nearest grid crossing (C04)",
            SegmentSource.GridLetter => "Nearest letter grid (C)",
            SegmentSource.GridNumber => "Nearest number grid (04)",
            SegmentSource.FixedText => "Fixed text",
            SegmentSource.ParameterValue => "Parameter value",
            SegmentSource.RunningNumber => "Running number",
            SegmentSource.ModelAuto => "Auto: Project Info, else file name",
            SegmentSource.ModelProjectParameter => "Project Information parameter",
            SegmentSource.ModelFileName => "Code in the file name",
            _ => source.ToString()
        };

        /// <summary>True when <paramref name="source"/> needs the Option text (a parameter name, text or digits).</summary>
        public static bool UsesOption(SegmentSource source) =>
            source == SegmentSource.ZoneParameterThenScopeBox || source == SegmentSource.ZoneParameter ||
            source == SegmentSource.LevelParameter || source == SegmentSource.FixedText ||
            source == SegmentSource.ParameterValue || source == SegmentSource.RunningNumber ||
            source == SegmentSource.ModelAuto || source == SegmentSource.ModelProjectParameter;

        /// <summary>True when the Option text is the name of a parameter the model must be read for.</summary>
        public static bool OptionIsParameterName(SegmentSource source) =>
            source == SegmentSource.ZoneParameterThenScopeBox || source == SegmentSource.ZoneParameter ||
            source == SegmentSource.LevelParameter || source == SegmentSource.ParameterValue ||
            source == SegmentSource.ModelAuto || source == SegmentSource.ModelProjectParameter;

        /// <summary>True when the Option text names a Project Information parameter.</summary>
        public static bool OptionIsProjectParameter(SegmentSource source) =>
            source == SegmentSource.ModelAuto || source == SegmentSource.ModelProjectParameter;

        /// <summary>Label shown before the Option box.</summary>
        public static string OptionLabel(SegmentSource source) => source switch
        {
            SegmentSource.ZoneParameterThenScopeBox or SegmentSource.ZoneParameter => "Parameter",
            SegmentSource.LevelParameter => "Level parameter",
            SegmentSource.FixedText => "Text",
            SegmentSource.ParameterValue => "Parameter",
            SegmentSource.RunningNumber => "Digits",
            SegmentSource.ModelAuto or SegmentSource.ModelProjectParameter => "Project Info",
            _ => string.Empty
        };

        /// <summary>Option value used when a source is first picked.</summary>
        public static string DefaultOption(SegmentSource source) => source switch
        {
            SegmentSource.ZoneParameterThenScopeBox or SegmentSource.ZoneParameter => "Zone",
            SegmentSource.LevelParameter => "Level Code",
            SegmentSource.FixedText => "NT02",
            SegmentSource.ParameterValue => "Workset",
            SegmentSource.RunningNumber => "4",
            SegmentSource.ModelAuto or SegmentSource.ModelProjectParameter => "Model Code",
            _ => string.Empty
        };

        /// <summary>Number of digits of a running number, from its Option text (1–9, default 4).</summary>
        public static int Digits(string option) =>
            int.TryParse(option, out int d) ? Math.Clamp(d, 1, 9) : 4;

        /// <summary>
        /// The format the tool starts with: Project-Model-Zone-Level-Type-Grid-Number,
        /// e.g. NT02-ARC-ZA-L01-D09-C04-0001. Custom 2 starts switched off.
        /// </summary>
        public static List<SegmentDefinition> DefaultSegments() => new()
        {
            new() { Kind = SegmentKind.Custom1, Source = SegmentSource.FixedText, Option = "NT02" },
            new() { Kind = SegmentKind.Model, Source = SegmentSource.ModelAuto, Option = "Model Code" },
            new() { Kind = SegmentKind.Zone, Source = SegmentSource.ZoneParameterThenScopeBox, Option = "Zone" },
            new() { Kind = SegmentKind.Level, Source = SegmentSource.LevelShortCode },
            new() { Kind = SegmentKind.Type, Source = SegmentSource.TypeMark },
            new() { Kind = SegmentKind.Grid, Source = SegmentSource.GridCrossing },
            new() { Kind = SegmentKind.Custom2, Enabled = false, Source = SegmentSource.ParameterValue, Option = "Workset" },
            new() { Kind = SegmentKind.Custom3, Source = SegmentSource.RunningNumber, Option = "4" }
        };

        /// <summary>
        /// Repairs a saved segment list: keeps the saved order, drops repeats and sources a segment does
        /// not offer (falling back to that segment's default), and adds any missing segment. A missing
        /// Model segment goes right after Custom 1 (or first), switched on, so IDs from a format saved
        /// before it existed become unique across models too.
        /// </summary>
        public static List<SegmentDefinition> Normalize(IEnumerable<SegmentDefinition> saved)
        {
            var defaults = DefaultSegments();
            var result = new List<SegmentDefinition>();

            foreach (var segment in saved ?? Enumerable.Empty<SegmentDefinition>())
            {
                if (segment == null || !Enum.IsDefined(typeof(SegmentKind), segment.Kind)) continue;
                if (result.Any(r => r.Kind == segment.Kind)) continue;

                var copy = segment.Clone();
                if (!SourcesFor(copy.Kind).Contains(copy.Source))
                {
                    var fallback = defaults.First(d => d.Kind == copy.Kind);
                    copy.Source = fallback.Source;
                    copy.Option = fallback.Option;
                }
                copy.Option ??= string.Empty;
                result.Add(copy);
            }

            foreach (var missing in defaults.Where(d => result.All(r => r.Kind != d.Kind)))
            {
                if (missing.Kind == SegmentKind.Model)
                    result.Insert(result.FindIndex(r => r.Kind == SegmentKind.Custom1) + 1, missing);
                else
                    result.Add(missing);
            }

            return result;
        }
    }
}
