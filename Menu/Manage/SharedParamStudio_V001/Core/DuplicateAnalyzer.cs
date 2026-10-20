using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Core
{
    /// <summary>What two parameters have in common.</summary>
    public enum DuplicateKind
    {
        /// <summary>Same GUID (Revit cannot load such a file reliably).</summary>
        SameGuid,

        /// <summary>Same name (case-insensitive) under different GUIDs.</summary>
        SameName
    }

    /// <summary>One parameter that belongs to a duplicate set.</summary>
    public sealed class DuplicateItem
    {
        /// <summary>Why the set was flagged.</summary>
        public DuplicateKind Kind { get; set; }

        /// <summary>The shared key (GUID or name) of the set.</summary>
        public string Key { get; set; }

        /// <summary>The parameter.</summary>
        public SpParam Param { get; set; }

        /// <summary>True for the first occurrence, which is the one kept when duplicates are removed.</summary>
        public bool IsFirst { get; set; }

        /// <summary>True when members of this set differ in data type, which makes the clash risky.</summary>
        public bool DataTypesDiffer { get; set; }
    }

    /// <summary>Result of <see cref="DuplicateAnalyzer.Analyze"/>.</summary>
    public sealed class DuplicateReport
    {
        /// <summary>Every member of every duplicate set, sets kept together.</summary>
        public List<DuplicateItem> Items { get; } = new List<DuplicateItem>();

        /// <summary>Number of parameters scanned.</summary>
        public int Scanned { get; set; }

        /// <summary>Number of duplicate sets.</summary>
        public int SetCount { get; set; }

        /// <summary>Parameters that would be removed (every non-first member).</summary>
        public IEnumerable<SpParam> Removable
            => Items.Where(i => !i.IsFirst).Select(i => i.Param).Distinct();
    }

    /// <summary>Finds duplicate GUIDs and duplicate names in a shared parameter file.</summary>
    public static class DuplicateAnalyzer
    {
        /// <summary>Scans <paramref name="file"/> and reports every duplicate set.</summary>
        public static DuplicateReport Analyze(SpFile file)
        {
            var report = new DuplicateReport { Scanned = file.Params.Count };
            AddSets(report, file.Params, DuplicateKind.SameGuid, p => SpKeys.GuidKey(p.Guid));
            AddSets(report, file.Params, DuplicateKind.SameName, p => SpKeys.NameKey(p.Name));
            return report;
        }

        /// <summary>
        /// Copy of <paramref name="file"/> without the non-first members of every duplicate set.
        /// The original is not changed.
        /// </summary>
        public static SpFile RemoveDuplicates(SpFile file, DuplicateReport report)
        {
            var drop = new HashSet<SpParam>(report.Removable);
            var copy = new SpFile
            {
                MetaVersion = file.MetaVersion,
                MetaMinVersion = file.MetaMinVersion,
                Encoding = file.Encoding
            };
            copy.Groups.AddRange(file.Groups);
            copy.Params.AddRange(file.Params.Where(p => !drop.Contains(p)));
            return copy;
        }

        private static void AddSets(DuplicateReport report, List<SpParam> all, DuplicateKind kind, System.Func<SpParam, string> key)
        {
            foreach (var set in all.GroupBy(key).Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                bool differ = set.Select(p => (p.DataType ?? string.Empty).ToUpperInvariant()).Distinct().Count() > 1;
                bool first = true;
                foreach (var p in set)
                {
                    report.Items.Add(new DuplicateItem
                    {
                        Kind = kind,
                        Key = kind == DuplicateKind.SameGuid ? set.Key : p.Name,
                        Param = p,
                        IsFirst = first,
                        DataTypesDiffer = differ
                    });
                    first = false;
                }
                report.SetCount++;
            }
        }
    }
}
