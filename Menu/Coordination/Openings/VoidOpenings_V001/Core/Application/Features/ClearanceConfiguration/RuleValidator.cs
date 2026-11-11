using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>Checks the user's clearance settings before a run and says what to fix.</summary>
    public static class RuleValidator
    {
        /// <summary>One message per problem; empty when the settings can be used.</summary>
        public static IReadOnlyList<string> Validate(VoidOpeningsSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var problems = new List<string>();

            for (int i = 0; i < settings.Rules.Count; i++)
            {
                ClearanceRuleDto rule = settings.Rules[i];
                string label = $"Rule {i + 1} ({NameOf(rule.Category)} {rule.FromMm:0.#}–{rule.ToMm:0.#} mm)";
                if (rule.FromMm < 0 || rule.ToMm < rule.FromMm)
                    problems.Add($"{label}: the size range must run from a smaller to a larger size, both 0 or more.");
                else if (rule.ClearanceMm < 0)
                    problems.Add($"{label}: the offset cannot be negative.");
            }

            foreach (IGrouping<string, ClearanceRuleDto> group in settings.Rules
                .Where(r => r.FromMm >= 0 && r.ToMm >= r.FromMm).GroupBy(r => r.Category ?? string.Empty))
            {
                List<ClearanceRuleDto> ordered = group.OrderBy(r => r.FromMm).ToList();
                for (int i = 1; i < ordered.Count; i++)
                    if (ordered[i].FromMm <= ordered[i - 1].ToMm)
                        problems.Add($"{NameOf(group.Key)}: the ranges {ordered[i - 1].FromMm:0.#}–{ordered[i - 1].ToMm:0.#} and " +
                                     $"{ordered[i].FromMm:0.#}–{ordered[i].ToMm:0.#} mm overlap.");
            }

            if (settings.DefaultClearanceMm < 0) problems.Add("The default offset cannot be negative.");
            if (settings.OvershootMm < 0) problems.Add("The overshoot cannot be negative.");
            if (settings.RoundUpToMm < 0) problems.Add("The rounding module cannot be negative.");
            return problems;
        }

        private static string NameOf(string category)
            => Enum.TryParse(category, true, out MepCategory value) ? CategoryCatalog.NameOf(value) : (category ?? "?");
    }

    /// <summary>Settings to and from JSON text.</summary>
    public static class SettingsSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };

        /// <summary>Writes the settings as JSON.</summary>
        public static string ToJson(VoidOpeningsSettings settings)
        {
            settings.Version = VoidOpeningsSettings.CurrentVersion;
            return JsonSerializer.Serialize(settings, Options);
        }

        /// <summary>
        /// Reads settings from JSON. Fields a file does not have keep their defaults, so older files
        /// load; text that is not JSON gives the default settings.
        /// </summary>
        public static VoidOpeningsSettings FromJson(string json)
        {
            try
            {
                VoidOpeningsSettings settings = JsonSerializer.Deserialize<VoidOpeningsSettings>(json ?? string.Empty, Options);
                if (settings == null)
                    return VoidOpeningsSettings.CreateDefault();
                settings.Rules ??= new List<ClearanceRuleDto>();
                settings.Version = VoidOpeningsSettings.CurrentVersion;
                return settings;
            }
            catch (JsonException)
            {
                return VoidOpeningsSettings.CreateDefault();
            }
        }
    }
}
