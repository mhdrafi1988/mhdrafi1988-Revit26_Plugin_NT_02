using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Infrastructure.Json
{
    /// <summary>
    /// The settings file format (JSON, "version" field). Version 1: the clearance rules, the default
    /// offset and the sizing options. Lengths are millimetres; enums are written by name; a rule's
    /// "toMm" is null when it has no upper limit.
    /// Reading is forgiving: a missing or wrong value falls back to its default, and a rule that
    /// cannot be understood is skipped, with a warning saying so.
    /// </summary>
    public static class SettingsJson
    {
        /// <summary>Version this build writes.</summary>
        public const int CurrentVersion = 1;

        private const double MaxMm = 100000;

        /// <summary>Writes a setup as indented JSON.</summary>
        public static string Write(ClearanceSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));

            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteNumber("version", CurrentVersion);
                json.WriteNumber("defaultClearanceMm", setup.DefaultClearanceMm);
                json.WriteString("defaultMode", setup.DefaultMode.ToString());
                json.WriteNumber("overshootMm", setup.Options.OvershootMm);
                json.WriteNumber("roundUpToMm", setup.Options.RoundUpToMm);
                json.WriteBoolean("includeInsulation", setup.Options.IncludeInsulation);
                json.WriteString("axisMode", setup.Options.AxisMode.ToString());
                json.WriteNumber("maxCircularAngleDeg", setup.Options.MaxCircularAngleDeg);
                json.WriteStartArray("rules");
                foreach (ClearanceRule rule in setup.Rules)
                {
                    json.WriteStartObject();
                    json.WriteString("category", rule.Category.ToString());
                    json.WriteNumber("fromMm", rule.FromMm);
                    if (rule.HasUpperLimit) json.WriteNumber("toMm", rule.ToMm);
                    else json.WriteNull("toMm");
                    json.WriteNumber("clearanceMm", rule.ClearanceMm);
                    json.WriteString("mode", rule.Mode.ToString());
                    json.WriteString("shape", rule.Shape.ToString());
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Reads a setup. <paramref name="warning"/> is null when everything was understood.
        /// Throws <see cref="JsonException"/> when the text is not a JSON object at all.
        /// </summary>
        public static ClearanceSetup Read(string json, out string warning)
        {
            var notes = new List<string>();
            var documentOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            using JsonDocument document = JsonDocument.Parse(json ?? string.Empty, documentOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("The settings file does not hold a JSON object.");

            int version = root.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 1;
            if (version > CurrentVersion)
                notes.Add($"the file was written by a newer version of the tool (format {version}); settings this version does not know are dropped when you save");

            ClearanceSetup defaults = ClearanceSetup.CreateDefault();
            var options = new OpeningSettings
            {
                OvershootMm = Length(root, "overshootMm", defaults.Options.OvershootMm, notes),
                RoundUpToMm = Length(root, "roundUpToMm", defaults.Options.RoundUpToMm, notes),
                IncludeInsulation = Bool(root, "includeInsulation", defaults.Options.IncludeInsulation, notes),
                AxisMode = EnumOf(root, "axisMode", defaults.Options.AxisMode, notes),
                MaxCircularAngleDeg = Number(root, "maxCircularAngleDeg", defaults.Options.MaxCircularAngleDeg, 0, 89, notes),
            };
            double defaultClearance = Length(root, "defaultClearanceMm", defaults.DefaultClearanceMm, notes);
            ClearanceMode defaultMode = EnumOf(root, "defaultMode", defaults.DefaultMode, notes);

            var rules = new List<ClearanceRule>();
            int skipped = 0;
            if (root.TryGetProperty("rules", out JsonElement list))
            {
                if (list.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in list.EnumerateArray())
                    {
                        ClearanceRule rule = Rule(item);
                        if (rule != null) rules.Add(rule);
                        else skipped++;
                    }
                }
                else if (list.ValueKind != JsonValueKind.Null)
                {
                    notes.Add("\"rules\" is not a list, so no rule was read");
                }
            }
            if (skipped > 0)
                notes.Add($"{skipped} rule(s) could not be understood and were skipped");

            warning = notes.Count == 0 ? null : string.Join("; ", notes) + ".";
            return new ClearanceSetup(rules, defaultClearance, defaultMode, options);
        }

        // A rule is all or nothing: half a rule would apply a clearance the user did not write.
        private static ClearanceRule Rule(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;
            if (!TryEnum(item, "category", out MepCategory category))
                return null;
            if (!TryLength(item, "clearanceMm", out double clearance))
                return null;

            double from = 0;
            if (Has(item, "fromMm") && !TryLength(item, "fromMm", out from))
                return null;
            double to = ClearanceRule.NoUpperLimit;
            if (Has(item, "toMm") && !TryLength(item, "toMm", out to))
                return null;
            if (from > to)
                return null;

            ClearanceMode mode = ClearanceMode.PerSide;
            if (Has(item, "mode") && !TryEnum(item, "mode", out mode))
                return null;
            OpeningShapePolicy shape = OpeningShapePolicy.Auto;
            if (Has(item, "shape") && !TryEnum(item, "shape", out shape))
                return null;

            return new ClearanceRule(category, from, to, clearance, mode, shape);
        }

        private static bool Has(JsonElement obj, string name)
            => obj.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

        private static bool TryLength(JsonElement obj, string name, out double value)
        {
            value = 0;
            return obj.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number
                && e.TryGetDouble(out value) && !double.IsNaN(value) && value >= 0 && value <= MaxMm;
        }

        // Numeric values of an enum are refused: only names are written, and a number could be out of range.
        private static bool TryEnum<T>(JsonElement obj, string name, out T value) where T : struct, Enum
        {
            value = default;
            if (!obj.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.String)
                return false;
            string text = e.GetString();
            return !string.IsNullOrWhiteSpace(text) && !char.IsDigit(text.Trim()[0]) && text.Trim()[0] != '-'
                && Enum.TryParse(text.Trim(), ignoreCase: true, out value) && Enum.IsDefined(typeof(T), value);
        }

        private static double Length(JsonElement root, string name, double fallback, List<string> notes)
            => Number(root, name, fallback, 0, MaxMm, notes);

        private static double Number(JsonElement root, string name, double fallback, double min, double max, List<string> notes)
        {
            if (!Has(root, name))
                return fallback;
            if (root.GetProperty(name).ValueKind == JsonValueKind.Number && root.GetProperty(name).TryGetDouble(out double value)
                && !double.IsNaN(value) && value >= min && value <= max)
                return value;
            notes.Add($"\"{name}\" is not valid, so its default is used");
            return fallback;
        }

        private static bool Bool(JsonElement root, string name, bool fallback, List<string> notes)
        {
            if (!Has(root, name))
                return fallback;
            JsonValueKind kind = root.GetProperty(name).ValueKind;
            if (kind == JsonValueKind.True) return true;
            if (kind == JsonValueKind.False) return false;
            notes.Add($"\"{name}\" is not valid, so its default is used");
            return fallback;
        }

        private static T EnumOf<T>(JsonElement root, string name, T fallback, List<string> notes) where T : struct, Enum
        {
            if (!Has(root, name))
                return fallback;
            if (TryEnum(root, name, out T value))
                return value;
            notes.Add($"\"{name}\" is not valid, so its default is used");
            return fallback;
        }
    }
}
