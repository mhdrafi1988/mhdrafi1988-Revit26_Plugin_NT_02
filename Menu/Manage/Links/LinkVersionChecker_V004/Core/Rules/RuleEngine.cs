using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Models;

namespace Revit26_Plugin_Revised.LinkVersionChecker_V004.Core.Rules
{
    /// <summary>Data every rule can see.</summary>
    public sealed class RuleContext
    {
        public int CurrentYear { get; }
        public bool InModel { get; }
        /// <summary>All links of the same host (for cross-link checks such as duplicates).</summary>
        public IReadOnlyList<LinkInfo> HostLinks { get; }

        public RuleContext(int currentYear, bool inModel, IReadOnlyList<LinkInfo> hostLinks)
        {
            CurrentYear = currentYear;
            InModel = inModel;
            HostLinks = hostLinks;
        }
    }

    /// <summary>
    /// A single check. To add a new check: implement this interface and add it to
    /// <see cref="RuleEngine.DefaultRules"/>. Rules only read facts — they never touch Revit.
    /// </summary>
    public interface ILinkRule
    {
        string Id { get; }
        IEnumerable<Finding> Evaluate(LinkInfo link, RuleContext ctx);
    }

    /// <summary>Tunable numbers behind the "Est. extra load time" column (rough heuristic).</summary>
    public static class LoadCostModel
    {
        public static double UpgradeBaseSeconds = 5;
        public static double UpgradeSecondsPerMb = 0.15;
        public static double UpgradeSecondsPerExtraVersion = 8;
        public static double IfcBaseSeconds = 10;
        public static double IfcSecondsPerMb = 0.35;
        public static double MissingLinkSeconds = 15;
        public static double DuplicateSecondsPerMb = 0.10;
    }

    public static class RuleEngine
    {
        public static readonly IReadOnlyList<ILinkRule> DefaultRules = new ILinkRule[]
        {
            new CloudLinkRule(),
            new AccOtherProjectRule(),
            new LegacyBim360Rule(),
            new DesktopConnectorRule(),
            new CloudSyncFolderRule(),
            new RemovableDriveRule(),
            new MappedDriveRule(),
            new RevitServerRule(),
            new UnresolvedSourceRule(),
            new MissingLinkRule(),
            new UnreadableHeaderRule(),
            new LaterVersionRule(),
            new TemporaryUpgradeRule(),
            new IfcCacheRule(),
            new DuplicateLinkRule(),
            new ImportedCadRule(),
            new LoadedPointCloudRule(),
            new AllWorksetsOpenRule(),
            new NestedLinkRule(),
            new AbsolutePathRule(),
        };

        /// <summary>Runs every rule on every link and fills Findings, Severity and EstSeconds.</summary>
        public static void Apply(IList<LinkInfo> links, int currentYear, bool inModel, IReadOnlyList<ILinkRule> rules = null)
        {
            rules ??= DefaultRules;

            foreach (var hostGroup in links.GroupBy(l => l.HostPath ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var hostLinks = hostGroup.ToList();
                var ctx = new RuleContext(currentYear, inModel, hostLinks);

                foreach (var link in hostLinks)
                {
                    var findings = new List<Finding>();
                    foreach (var rule in rules)
                    {
                        try
                        {
                            findings.AddRange(rule.Evaluate(link, ctx) ?? Enumerable.Empty<Finding>());
                        }
                        catch (Exception ex)
                        {
                            findings.Add(new Finding(rule.Id, Severity.Info, "Rule error", $"{rule.Id} failed: {ex.Message}"));
                        }
                    }

                    link.Findings = findings;
                    link.Severity = findings.Count == 0 ? Severity.Ok : findings.Max(f => f.Severity);
                    link.EstSeconds = link.IsUnloaded ? 0 : findings.Sum(f => f.EstSeconds);
                    link.NotifyDerived();
                }
            }
        }
    }
}
