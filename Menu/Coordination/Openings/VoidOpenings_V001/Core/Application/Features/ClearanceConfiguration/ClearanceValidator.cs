using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ModelSelection;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>
    /// Checks the OPENING CLEARANCE card and turns it into a <see cref="ClearanceSetup"/>.
    /// Errors: a value that is not a number from 0 to 100 000, a missing offset, "Size from" above
    /// "Size to". Warning: two rules of one category that cover the same sizes (the upper row wins).
    /// </summary>
    public static class ClearanceValidator
    {
        private const string Range = "a number from 0 to 100000";

        /// <summary>Checks <paramref name="input"/>.</summary>
        public static ClearanceValidation Validate(ClearanceInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var issues = new List<ClearanceIssue>();
            var rules = new List<ClearanceRule>();
            var rowOfRule = new List<int>();

            for (int row = 0; row < input.Rules.Count; row++)
            {
                ClearanceRule rule = ReadRule(input.Rules[row], row, issues);
                if (rule == null)
                    continue;
                rules.Add(rule);
                rowOfRule.Add(row);
            }

            AddOverlapWarnings(rules, rowOfRule, issues);

            bool generalOk = true;
            generalOk &= ReadRequired(input.DefaultClearanceText, "Default offset", ClearanceIssue.General, issues, out double defaultMm);
            generalOk &= ReadRequired(input.OvershootText, "Overshoot", ClearanceIssue.General, issues, out double overshootMm);
            generalOk &= ReadOptional(input.RoundUpToText, 0, "Round up to", " or empty for no rounding", ClearanceIssue.General, issues, out double roundUpMm);

            if (!generalOk || rules.Count != input.Rules.Count)
                return new ClearanceValidation(issues, null);

            var options = new OpeningSettings
            {
                OvershootMm = overshootMm,
                RoundUpToMm = roundUpMm,
                IncludeInsulation = input.IncludeInsulation,
                AxisMode = input.AxisMode,
                MaxCircularAngleDeg = input.MaxCircularAngleDeg,
            };
            return new ClearanceValidation(issues, new ClearanceSetup(rules, defaultMm, input.DefaultMode, options));
        }

        private static ClearanceRule ReadRule(ClearanceRuleInput row, int index, List<ClearanceIssue> issues)
        {
            if (row == null)
            {
                issues.Add(new ClearanceIssue(index, IssueSeverity.Error, "The rule is empty."));
                return null;
            }

            bool ok = true;
            ok &= ReadOptional(row.FromText, 0, "Size from", " or empty for 0", index, issues, out double fromMm);
            ok &= ReadOptional(row.ToText, ClearanceRule.NoUpperLimit, "Size to", " or empty for no upper limit", index, issues, out double toMm);
            ok &= ReadRequired(row.ClearanceText, "Offset", index, issues, out double clearanceMm);
            if (!ok)
                return null;

            if (fromMm > toMm)
            {
                issues.Add(new ClearanceIssue(index, IssueSeverity.Error, "Size from is larger than Size to."));
                return null;
            }
            return new ClearanceRule(row.Category, fromMm, toMm, clearanceMm, row.Mode, row.Shape);
        }

        private static bool ReadRequired(string text, string label, int index, List<ClearanceIssue> issues, out double valueMm)
        {
            if (MmText.TryParse(text, out valueMm))
                return true;
            issues.Add(new ClearanceIssue(index, IssueSeverity.Error,
                MmText.IsBlank(text) ? $"{label} is required." : $"{label} must be {Range}."));
            return false;
        }

        private static bool ReadOptional(string text, double whenBlank, string label, string blankHint, int index,
            List<ClearanceIssue> issues, out double valueMm)
        {
            if (MmText.IsBlank(text))
            {
                valueMm = whenBlank;
                return true;
            }
            if (MmText.TryParse(text, out valueMm))
                return true;
            issues.Add(new ClearanceIssue(index, IssueSeverity.Error, $"{label} must be {Range},{blankHint}."));
            return false;
        }

        // Two rules that only share an end point (0-25 and 25-50) are a normal way to write
        // ranges, so they are not reported; the upper row is used at the shared size.
        private static void AddOverlapWarnings(List<ClearanceRule> rules, List<int> rowOfRule, List<ClearanceIssue> issues)
        {
            for (int later = 1; later < rules.Count; later++)
            {
                for (int earlier = 0; earlier < later; earlier++)
                {
                    ClearanceRule a = rules[earlier];
                    ClearanceRule b = rules[later];
                    if (a.Category != b.Category)
                        continue;

                    double from = Math.Max(a.FromMm, b.FromMm);
                    double to = Math.Min(a.ToMm, b.ToMm);
                    if (from >= to)
                        continue;

                    string sizes = to < ClearanceRule.NoUpperLimit
                        ? $"{MmText.Format(from)} to {MmText.Format(to)} mm"
                        : $"{MmText.Format(from)} mm and above";
                    issues.Add(new ClearanceIssue(rowOfRule[later], IssueSeverity.Warning,
                        $"Rule {rowOfRule[earlier] + 1} already covers {CategoryCatalog.NameOf(b.Category)} {sizes}; rule {rowOfRule[earlier] + 1} is used there."));
                    break; // one warning per row is enough
                }
            }
        }
    }
}
