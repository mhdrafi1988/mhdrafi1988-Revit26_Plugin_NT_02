using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Core
{
    /// <summary>How the value written to the parameter is built from the level name and the offset.</summary>
    public enum ValueFormat
    {
        /// <summary>"Level 02 (+450)" — the 1.0.x format and the default.</summary>
        LevelParenOffset = 0,
        /// <summary>"Level 02" — offset ignored.</summary>
        LevelOnly,
        /// <summary>"Level 02 +450".</summary>
        LevelSpaceOffset,
        /// <summary>"Level 02 (+450 mm)".</summary>
        LevelParenOffsetMm,
        /// <summary>"Level 02 / +450".</summary>
        LevelSlashOffset,
        /// <summary>"Level 02 [+450]".</summary>
        LevelBracketOffset,
        /// <summary>"Level 02, Offset +450".</summary>
        LevelCommaOffset,
        /// <summary>Like <see cref="LevelParenOffset"/>, but "Level 01" when the offset rounds to 0.</summary>
        LevelParenOffsetNoZero,
        /// <summary>A user pattern with the tokens {Level} and {Offset}.</summary>
        Custom
    }

    /// <summary>One entry of the Pattern list.</summary>
    public sealed class ValuePatternOption
    {
        /// <summary>Creates an entry.</summary>
        public ValuePatternOption(ValueFormat format, string label)
        {
            Format = format;
            Label = label;
        }

        /// <summary>The format.</summary>
        public ValueFormat Format { get; }

        /// <summary>Text shown in the list (a sample value).</summary>
        public string Label { get; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    /// <summary>Builds the value text in each <see cref="ValueFormat"/>.</summary>
    public static class ValueFormatter
    {
        /// <summary>Token for the level name in a custom pattern.</summary>
        public const string LevelToken = "{Level}";

        /// <summary>Token for the signed offset in a custom pattern.</summary>
        public const string OffsetToken = "{Offset}";

        /// <summary>Default custom pattern (same result as the default format).</summary>
        public const string DefaultCustomPattern = "{Level} ({Offset})";

        /// <summary>Patterns for "Level name + offset", in list order (everything except <see cref="ValueFormat.LevelOnly"/>).</summary>
        public static IReadOnlyList<ValuePatternOption> OffsetPatterns { get; } = new[]
        {
            new ValuePatternOption(ValueFormat.LevelParenOffset, "Level 02 (+450)"),
            new ValuePatternOption(ValueFormat.LevelSpaceOffset, "Level 02 +450"),
            new ValuePatternOption(ValueFormat.LevelParenOffsetMm, "Level 02 (+450 mm)"),
            new ValuePatternOption(ValueFormat.LevelSlashOffset, "Level 02 / +450"),
            new ValuePatternOption(ValueFormat.LevelBracketOffset, "Level 02 [+450]"),
            new ValuePatternOption(ValueFormat.LevelCommaOffset, "Level 02, Offset +450"),
            new ValuePatternOption(ValueFormat.LevelParenOffsetNoZero, "Level 02 (+450) · no “(0)”"),
            new ValuePatternOption(ValueFormat.Custom, "Custom…"),
        };

        /// <summary>
        /// The value for <paramref name="levelName"/> and <paramref name="offsetMm"/> in <paramref name="format"/>.
        /// <paramref name="customPattern"/> is used only for <see cref="ValueFormat.Custom"/>.
        /// </summary>
        public static string Format(string levelName, double offsetMm, ValueFormat format, string customPattern = null)
        {
            string level = levelName ?? string.Empty;
            string offset = BaseLevelRules.FormatOffset(offsetMm);
            return format switch
            {
                ValueFormat.LevelOnly => level,
                ValueFormat.LevelSpaceOffset => $"{level} {offset}",
                ValueFormat.LevelParenOffsetMm => $"{level} ({offset} mm)",
                ValueFormat.LevelSlashOffset => $"{level} / {offset}",
                ValueFormat.LevelBracketOffset => $"{level} [{offset}]",
                ValueFormat.LevelCommaOffset => $"{level}, Offset {offset}",
                ValueFormat.LevelParenOffsetNoZero => offset == "0" ? level : $"{level} ({offset})",
                ValueFormat.Custom => (customPattern ?? DefaultCustomPattern)
                    .Replace(LevelToken, level, StringComparison.OrdinalIgnoreCase)
                    .Replace(OffsetToken, offset, StringComparison.OrdinalIgnoreCase),
                _ => $"{level} ({offset})"
            };
        }

        /// <summary>
        /// Checks a custom pattern: not empty, contains {Level}, and no braces other than the two tokens.
        /// Returns null when it is fine, else the reason.
        /// </summary>
        public static string ValidateCustom(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return "The custom pattern is empty.";
            if (pattern.IndexOf(LevelToken, StringComparison.OrdinalIgnoreCase) < 0) return "The custom pattern must contain {Level}.";
            string rest = pattern.Replace(LevelToken, string.Empty, StringComparison.OrdinalIgnoreCase)
                                 .Replace(OffsetToken, string.Empty, StringComparison.OrdinalIgnoreCase);
            if (rest.IndexOf('{') >= 0 || rest.IndexOf('}') >= 0)
                return "The custom pattern has a brace that is not part of {Level} or {Offset}.";
            return null;
        }

        /// <summary>Sample line for the window, for offsets +450, 0 and −150.</summary>
        public static string Sample(ValueFormat format, string customPattern) =>
            $"{Format("Level 02", 450, format, customPattern)}  ·  {Format("Level 01", 0, format, customPattern)}  ·  {Format("Level 01", -150, format, customPattern)}";
    }
}
