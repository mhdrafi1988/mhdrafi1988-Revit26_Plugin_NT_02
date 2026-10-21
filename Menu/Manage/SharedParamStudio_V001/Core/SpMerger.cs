using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.Core
{
    /// <summary>What to do when both files define the same parameter differently.</summary>
    public enum ConflictPolicy
    {
        /// <summary>Keep the parameter from the first file.</summary>
        KeepFirst,

        /// <summary>Use the parameter from the second file.</summary>
        KeepSecond,

        /// <summary>Keep both: the second is renamed with a numeric suffix. A shared GUID cannot be kept twice, so those fall back to <see cref="KeepFirst"/>.</summary>
        KeepBoth
    }

    /// <summary>One line of the merge report.</summary>
    public sealed class MergeNote
    {
        /// <summary>True for conflicts, skipped new parameters and other things the user should read.</summary>
        public bool IsWarning { get; set; }

        /// <summary>Text shown in the log and preview.</summary>
        public string Message { get; set; }
    }

    /// <summary>Outcome of <see cref="SpMerger.Merge"/>; nothing is written to disk.</summary>
    public sealed class MergeResult
    {
        /// <summary>The merged file, ready for <see cref="SpFileWriter.Write"/>.</summary>
        public SpFile Merged { get; set; }

        /// <summary>Parameters taken from the first file.</summary>
        public int FromFirst { get; set; }

        /// <summary>Parameters of the second file added as new.</summary>
        public int AddedFromSecond { get; set; }

        /// <summary>Parameters present in both files with the same GUID, name and type.</summary>
        public int Identical { get; set; }

        /// <summary>Conflicting pairs (resolved by the policy).</summary>
        public int Conflicts { get; set; }

        /// <summary>New parameters appended on top of the merge.</summary>
        public int NewParameters { get; set; }

        /// <summary>Report lines in processing order.</summary>
        public List<MergeNote> Notes { get; } = new List<MergeNote>();
    }

    /// <summary>Merges two shared parameter files. GUIDs are never changed.</summary>
    public static class SpMerger
    {
        /// <summary>
        /// Merges <paramref name="second"/> into a copy of <paramref name="first"/>, then appends
        /// <paramref name="newParams"/> (a blank GUID gets a fresh one). Names that clash with an existing
        /// parameter, and invalid names, are skipped and reported, never overwritten.
        /// </summary>
        public static MergeResult Merge(SpFile first, SpFile second, ConflictPolicy policy, IEnumerable<SpParam> newParams = null)
        {
            var result = new MergeResult();
            var merged = new SpFile
            {
                MetaVersion = first.MetaVersion,
                MetaMinVersion = first.MetaMinVersion,
                Encoding = first.Encoding
            };
            foreach (var g in first.Groups.Concat(second.Groups)) merged.EnsureGroup(g);
            result.Merged = merged;

            var byGuid = new Dictionary<string, SpParam>();
            var byName = new Dictionary<string, SpParam>();

            void Index(SpParam p)
            {
                byGuid[SpKeys.GuidKey(p.Guid)] = p;
                string n = SpKeys.NameKey(p.Name);
                if (!byName.ContainsKey(n)) byName[n] = p;
            }

            void Reindex()
            {
                byGuid.Clear();
                byName.Clear();
                foreach (var p in merged.Params) Index(p);
            }

            foreach (var p in first.Params)
            {
                if (byGuid.ContainsKey(SpKeys.GuidKey(p.Guid)))
                {
                    result.Notes.Add(Warn($"First file lists GUID {p.Guid} twice ('{p.Name}', line {p.LineNumber}); extra copy dropped."));
                    continue;
                }
                if (byName.ContainsKey(SpKeys.NameKey(p.Name)))
                    result.Notes.Add(Warn($"First file already has two parameters named '{p.Name}' with different GUIDs; both kept."));
                var copy = p.Clone();
                merged.Params.Add(copy);
                Index(copy);
                result.FromFirst++;
            }

            foreach (var p in second.Params)
            {
                string gk = SpKeys.GuidKey(p.Guid);
                string nk = SpKeys.NameKey(p.Name);

                if (byGuid.TryGetValue(gk, out var sameGuid))
                {
                    if (SpKeys.NameKey(sameGuid.Name) == nk &&
                        string.Equals(sameGuid.DataType, p.DataType, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Identical++;
                        continue;
                    }

                    result.Conflicts++;
                    if (policy == ConflictPolicy.KeepSecond)
                    {
                        int i = merged.Params.IndexOf(sameGuid);
                        merged.Params[i] = p.Clone();
                        Reindex();
                        result.Notes.Add(Warn($"GUID {p.Guid}: '{sameGuid.Name}' ({sameGuid.DataType}) replaced by '{p.Name}' ({p.DataType}) from the second file."));
                    }
                    else
                    {
                        result.Notes.Add(Warn($"GUID {p.Guid}: kept '{sameGuid.Name}' ({sameGuid.DataType}) from the first file; second file calls it '{p.Name}' ({p.DataType})."));
                    }
                    continue;
                }

                if (byName.TryGetValue(nk, out var sameName))
                {
                    result.Conflicts++;
                    switch (policy)
                    {
                        case ConflictPolicy.KeepSecond:
                            merged.Params[merged.Params.IndexOf(sameName)] = p.Clone();
                            Reindex();
                            result.Notes.Add(Warn($"Name '{p.Name}': first file's version ({sameName.DataType}, {sameName.Guid}) replaced by the second file's ({p.DataType}, {p.Guid})."));
                            break;

                        case ConflictPolicy.KeepBoth:
                            var renamed = p.Clone();
                            renamed.Name = UniqueName(p.Name, byName);
                            merged.Params.Add(renamed);
                            Index(renamed);
                            merged.EnsureGroup(renamed.GroupName);
                            result.AddedFromSecond++;
                            result.Notes.Add(Warn($"Name '{p.Name}' exists with another GUID; second file's copy added as '{renamed.Name}'."));
                            break;

                        default:
                            result.Notes.Add(Warn($"Name '{p.Name}' exists with another GUID; second file's copy ({p.Guid}) skipped."));
                            break;
                    }
                    continue;
                }

                var add = p.Clone();
                merged.Params.Add(add);
                Index(add);
                merged.EnsureGroup(add.GroupName);
                result.AddedFromSecond++;
            }

            foreach (var np in newParams ?? Enumerable.Empty<SpParam>())
            {
                string bad = SpKeys.ValidateName(np.Name);
                if (bad != null)
                {
                    result.Notes.Add(Warn($"New parameter '{np.Name}' skipped: {bad}"));
                    continue;
                }
                if (byName.ContainsKey(SpKeys.NameKey(np.Name)))
                {
                    result.Notes.Add(Warn($"New parameter '{np.Name}' skipped: a parameter with that name is already in the merged file."));
                    continue;
                }

                var add = np.Clone();
                if (string.IsNullOrWhiteSpace(add.Guid)) add.Guid = System.Guid.NewGuid().ToString();
                if (string.IsNullOrWhiteSpace(add.GroupName)) add.GroupName = "Other";
                if (byGuid.ContainsKey(SpKeys.GuidKey(add.Guid)))
                {
                    result.Notes.Add(Warn($"New parameter '{np.Name}' skipped: its GUID is already in the merged file."));
                    continue;
                }
                merged.Params.Add(add);
                merged.EnsureGroup(add.GroupName);
                Index(add);
                result.NewParameters++;
                result.Notes.Add(new MergeNote { Message = $"Added new parameter '{add.Name}' ({add.DataType}) to group '{add.GroupName}'." });
            }

            return result;
        }

        private static MergeNote Warn(string message) => new MergeNote { IsWarning = true, Message = message };

        private static string UniqueName(string name, Dictionary<string, SpParam> byName)
        {
            for (int i = 2; ; i++)
            {
                string candidate = $"{name}_{i}";
                if (!byName.ContainsKey(SpKeys.NameKey(candidate))) return candidate;
            }
        }
    }
}
