using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Rules;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Features.ClearanceConfiguration
{
    /// <summary>How serious a clearance issue is.</summary>
    public enum IssueSeverity
    {
        /// <summary>Worth knowing; the setup can still be saved and used.</summary>
        Warning,
        /// <summary>The setup cannot be saved or used until this is fixed.</summary>
        Error,
    }

    /// <summary>One problem found on the OPENING CLEARANCE card.</summary>
    public sealed class ClearanceIssue
    {
        /// <summary><see cref="RuleIndex"/> of an issue that is not about a rule row.</summary>
        public const int General = -1;

        /// <summary>Zero-based row of the rule, or <see cref="General"/>.</summary>
        public int RuleIndex { get; }

        /// <summary>Error or warning.</summary>
        public IssueSeverity Severity { get; }

        /// <summary>What is wrong, in the user's words.</summary>
        public string Message { get; }

        /// <summary>Creates an issue.</summary>
        public ClearanceIssue(int ruleIndex, IssueSeverity severity, string message)
        {
            RuleIndex = ruleIndex;
            Severity = severity;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>Result of checking the card: the issues and, when there is no error, the setup.</summary>
    public sealed class ClearanceValidation
    {
        /// <summary>Every issue found, rule rows first.</summary>
        public IReadOnlyList<ClearanceIssue> Issues { get; }

        /// <summary>The validated setup; null when there is at least one error.</summary>
        public ClearanceSetup Setup { get; }

        /// <summary>Creates a result.</summary>
        public ClearanceValidation(IEnumerable<ClearanceIssue> issues, ClearanceSetup setup)
        {
            Issues = (issues ?? Enumerable.Empty<ClearanceIssue>()).ToList();
            if (HasErrors && setup != null)
                throw new ArgumentException("A setup with errors cannot be used.", nameof(setup));
            Setup = setup;
        }

        /// <summary>True when something must be fixed before the setup can be saved.</summary>
        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

        /// <summary>Number of errors.</summary>
        public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);

        /// <summary>Number of warnings.</summary>
        public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);

        /// <summary>Issues of one rule row, or of the card itself for <see cref="ClearanceIssue.General"/>.</summary>
        public IReadOnlyList<ClearanceIssue> IssuesFor(int ruleIndex) => Issues.Where(i => i.RuleIndex == ruleIndex).ToList();
    }
}
